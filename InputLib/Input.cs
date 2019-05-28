using System;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using OpenTK.Input;


namespace InputLib
{
	public class Input
	{
		public class InputAction
		{
			public long	mTimeHeldMS;	//time action held since last action or initially
			public Enum	mAction;		//user specified thing?

			internal InputAction(long timeHeld, Enum act)
			{
				//scale time to MS
				mTimeHeldMS	=timeHeld / (Stopwatch.Frequency / 1000);
				mAction		=act;
			}
		}

		internal class KeyHeldInfo
		{
			internal long	mInitialPressTime;
			internal long	mTimeHeld;
			internal Key	mKey;


			internal KeyHeldInfo(){}

			internal KeyHeldInfo(KeyHeldInfo copyMe)
			{
				mInitialPressTime	=copyMe.mInitialPressTime;
				mTimeHeld			=copyMe.mTimeHeld;
				mKey				=copyMe.mKey;
			}
		}

		internal class MouseMovementInfo
		{
			internal IntPtr	mDevice;
			internal int	mXMove, mYMove;
		}

		//this stuff piles up between updates
		//via the handlers
		List<KeyHeldInfo>	mKeysHeld	=new List<KeyHeldInfo>();
		List<KeyHeldInfo>	mKeysUp		=new List<KeyHeldInfo>();

		//mappings to controllers / keys / mice / whatever
		List<ActionMapping>	mActionMap	=new List<ActionMapping>();

		//active toggles
		List<ActionMapping>	mActiveToggles	=new List<ActionMapping>();

		//fired actions (for once per press activations)
		List<ActionMapping>	mOnceActives	=new List<ActionMapping>();

		//for a press & release action, to ensure the combo was down
		List<ActionMapping>	mWasHeld	=new List<ActionMapping>();

		long	mLastUpdateTime;


		public Input()
		{
		}


		public void FreeAll()
		{
		}


		//in case of big delta times in a game, like sitting at a breakpoint
		//or a big drop in framerate
		//this is only needed for ActionTypes.ContinuousHold
		public void ClampInputTimes(long amount)
		{
			foreach(KeyHeldInfo key in mKeysHeld)
			{
				key.mTimeHeld =Math.Min(key.mTimeHeld, amount);
			}
		}


		void AddHeldKey(Key key, long ts, bool bUp)
		{
			bool	bFoundHeld	=false;
			foreach(KeyHeldInfo khi in mKeysHeld)
			{
				if(khi.mKey == key)
				{
					bFoundHeld		=true;
					khi.mTimeHeld	=ts - khi.mInitialPressTime;
					if(bUp)
					{
						bool	bFoundUp	=false;
						foreach(KeyHeldInfo khiu in mKeysUp)
						{
							if(khiu.mKey == key)
							{
								khiu.mTimeHeld	=khi.mTimeHeld;
								bFoundUp		=true;
							}
						}
						if(!bFoundUp)
						{
							mKeysUp.Add(khi);
						}
						mKeysHeld.Remove(khi);
					}
				}
			}
			if(!bFoundHeld)
			{
				if(bUp)
				{
					//Land here after a ctrl-alt-del with code 1
					return;
				}
				KeyHeldInfo	kh			=new KeyHeldInfo();
				kh.mInitialPressTime	=ts;
				kh.mTimeHeld			=0;
				kh.mKey					=key;

				mKeysHeld.Add(kh);
			}
		}


		public void ClearInputs()
		{
			mKeysHeld.Clear();
			mKeysUp.Clear();
			mActiveToggles.Clear();
			mOnceActives.Clear();
			mWasHeld.Clear();
		}


		void Update()
		{
			long	ts	=Stopwatch.GetTimestamp();

			//check existing helds
			KeyboardState	kbs	=Keyboard.GetState();

			List<KeyHeldInfo>	up	=new List<KeyHeldInfo>();

			foreach(KeyHeldInfo khi in mKeysHeld)
			{
				//update timing
				khi.mTimeHeld	=ts - khi.mInitialPressTime;

				if(kbs.IsKeyDown(khi.mKey))
				{
					//still held
				}
				else
				{
					up.Add(khi);	//released
				}
			}

			//move into up
			foreach(KeyHeldInfo khi in up)
			{
				mKeysHeld.Remove(khi);
				mKeysUp.Add(khi);
			}

			//check for new helds
			foreach(ActionMapping am in mActionMap)
			{
				bool	bAllDown	=true;
				foreach(Key k in am.mKeys)
				{
					if(!kbs.IsKeyDown(k))
					{
						bAllDown	=false;
						break;
					}
				}

				if(bAllDown)				
				{
					foreach(Key k in am.mKeys)
					{
						AddHeldKey(k, ts, false);
					}
				}
			}
		}


		public List<InputAction> GetAction()
		{
			Update();

			List<InputAction>	ret	=ComputeActions();

			mLastUpdateTime	=Stopwatch.GetTimestamp();

			return	ret;
		}


		static bool ListsMatch(List<Key> A, List<Key> B)
		{
			foreach(Key k in A)
			{
				if(!B.Contains(k))
				{
					return	false;
				}
			}
			return	true;
		}


		bool IsActionMapped(List<Key> keys, out ActionMapping mapped)
		{
			mapped	=null;
			foreach(ActionMapping am in mActionMap)
			{
				if(ListsMatch(am.mKeys, keys))
				{
					mapped	=am;
					return	true;
				}
			}
			return	false;
		}


		public void MapAction(Enum action,
			ActionTypes mode, List<Key> keys)
		{
			Debug.Assert(mode != ActionTypes.Toggle);

			ActionMapping	am;
			if(IsActionMapped(keys, out am))
			{
				//overwrite existing?
				am.mAction		=action;
				am.mActionType	=mode;
			}
			else
			{
				ActionMapping	amap	=new ActionMapping();

				amap.mAction		=action;
				amap.mActionType	=mode;
				amap.mKeys			=new List<Key>(keys);

				mActionMap.Add(amap);
			}
		}


		public void MapToggleAction(Enum action,
			Enum actionOff, List<Key> keys)
		{
			ActionMapping	am;
			if(IsActionMapped(keys, out am))
			{
				//overwrite existing?
				am.mAction		=action;
				am.mActionOff	=actionOff;
				am.mActionType	=ActionTypes.Toggle;
			}
			else
			{
				ActionMapping	amap	=new ActionMapping();

				amap.mAction		=action;
				amap.mActionOff		=actionOff;
				amap.mActionType	=ActionTypes.Toggle;
				amap.mKeys			=new List<Key>(keys);

				mActionMap.Add(amap);
			}
		}


		public void UnMapAction(List<Key> keys)
		{
			ActionMapping	am;
			if(IsActionMapped(keys, out am))
			{
				mActionMap.Remove(am);
			}
		}


		static bool IsActionInList(ActionMapping am, List<KeyHeldInfo> theList)
		{
			foreach(Key k in am.mKeys)
			{
				bool	bFound	=false;
				foreach(KeyHeldInfo khi in theList)
				{
					if(khi.mKey == k)
					{
						bFound	=true;
						break;
					}
				}

				if(!bFound)
				{
					return	false;
				}
			}
			return	true;
		}


		static long	GetMinTimeActionListed(ActionMapping am, List<KeyHeldInfo> theList)
		{
			long	minTime	=long.MaxValue;
			foreach(Key k in am.mKeys)
			{
				foreach(KeyHeldInfo khi in theList)
				{
					if(khi.mKey == k)
					{
						minTime	=Math.Min(khi.mTimeHeld, minTime);
						break;
					}
				}
			}

			if(minTime == long.MaxValue)
			{
				minTime	=0;
			}
			return	minTime;
		}


		List<InputAction> ComputeActions()
		{
			List<InputAction>	acts	=new List<InputAction>();

			long	ts	=Stopwatch.GetTimestamp();

			foreach(ActionMapping am in mActionMap)
			{
				if(IsActionInList(am, mKeysHeld))
				{
					if(am.mActionType == ActionTypes.ActivateOnce)
					{
						if(!mOnceActives.Contains(am))
						{
							//not yet fired
							InputAction	act	=new InputAction(0, am.mAction);
							acts.Add(act);

							mOnceActives.Add(am);
						}
						continue;
					}
					else if(am.mActionType == ActionTypes.Toggle)
					{
						if(!mActiveToggles.Contains(am))
						{
							InputAction	act	=new InputAction(0, am.mAction);
							acts.Add(act);

							mActiveToggles.Add(am);							
						}
						continue;
					}
					else if(am.mActionType == ActionTypes.PressAndRelease)
					{
						if(!mWasHeld.Contains(am))
						{
							mWasHeld.Add(am);
						}
						continue;
					}

					//some time should have passed for the remaining two types
					long	minTime	=GetMinTimeActionListed(am, mKeysHeld);
					if(minTime == 0)
					{
						continue;
					}

					if(am.mActionType == ActionTypes.ContinuousHold)
					{
						InputAction	act	=new InputAction(ts - mLastUpdateTime, am.mAction);
						acts.Add(act);
					}
					else if(am.mActionType == ActionTypes.AnalogAmount)
					{						
						Debug.Assert(false);
					}
					else
					{
						Debug.Assert(false);
					}
				}
				else if(IsActionInList(am, mKeysUp))
				{
					long	timeHeld	=GetMinTimeActionListed(am, mKeysUp);
					if(am.mActionType == ActionTypes.ContinuousHold)
					{
						continue;	//nothing to do
					}
					else if(am.mActionType == ActionTypes.PressAndRelease)
					{
						if(mWasHeld.Contains(am))
						{
							//release action
							InputAction	act	=new InputAction(ts - mLastUpdateTime, am.mAction);
							acts.Add(act);
							mWasHeld.Remove(am);
						}
					}
					else if(am.mActionType == ActionTypes.ActivateOnce)
					{
						if(mOnceActives.Contains(am))
						{
							mOnceActives.Remove(am);	//allow action again
						}
					}
					else if(am.mActionType == ActionTypes.AnalogAmount)
					{
						Debug.Assert(false);
					}
					else if(am.mActionType == ActionTypes.Toggle)
					{
						if(mActiveToggles.Contains(am))
						{
							//up action
							InputAction	act	=new InputAction(ts - mLastUpdateTime, am.mActionOff);
							acts.Add(act);
							mActiveToggles.Remove(am);
						}
					}
				}
			}

			mKeysUp.Clear();

			return	acts;
		}
	}
}
