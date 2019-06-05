using System;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;

using GLFW;
using VulkanCore;
using VulkanCore.Khr;

using InputLib;
using UtilityLib;

using Monitor	=GLFW.Monitor;


namespace DrunkSpock
{
	public class GameWindow
	{
		Window		mWnd;
		SurfaceKhr	mSurface;

		Input	mAttachedInput;

		bool	bDestroyed;

		//timings
		long	mLastFrame;
		long	mRenderAccum, mGameAccum;
		long	mUpdateTicMS	=33;
		long	mRenderTicMS	=33; 

		public event EventHandler	eGameTic;
		public event EventHandler	eRenderTic;
		public event EventHandler	eWindowClosing;


		public GameWindow(DrunkSpock ds, bool bResizable, string title,
			int width, int height, int monitorIndex, bool bCenter)
		{
			//set GLFW_NO_API
			Glfw.WindowHint(Hint.ClientApi, 0);
			Glfw.WindowHint(Hint.Resizable, bResizable);

			if(monitorIndex < 0 || monitorIndex >= Glfw.Monitors.Length)
			{
				monitorIndex	=0;
			}

			//my linux implementation has no monitor work area function
			List<int>	xCoords	=new List<int>();
			List<int>	yCoords	=new List<int>();
			for(int i=0;i < Glfw.Monitors.Length;i++)
			{
				int	mx, my;
				Glfw.GetMonitorPosition(Glfw.Monitors[i], out mx, out my);

				xCoords.Add(mx);
				yCoords.Add(my);
			}

			mWnd	=Glfw.CreateWindow(width, height, title,
						Monitor.None, Window.None);

			int	x, y;
			Glfw.GetWindowPosition(mWnd, out x, out y);

			//figure out which monitor the window is on
			int	bestMonitorIndex	=-1;
			int	bestDist			=Int32.MaxValue;
			for(int i=0;i < Glfw.Monitors.Length;i++)
			{
				if(x > xCoords[i])
				{
					int	dist	=x - xCoords[i];
					if(dist < bestDist)
					{
						bestMonitorIndex	=i;
					}
				}
			}

			//get current vidmode of monitors
			List<VideoMode>	modes	=new List<VideoMode>();
			for(int i=0;i < Glfw.Monitors.Length;i++)
			{
				modes.Add(Glfw.GetVideoMode(Glfw.Monitors[i]));
			}

			if(bCenter)
			{
				//center of desired monitor
				x	=modes[monitorIndex].Width / 2;
				y	=modes[monitorIndex].Height / 2;

				//adjust back by half window size
				x	-=width / 2;
				y	-=height / 2;
			}
			else
			{
				//adjust to local coordiates
				x	-=xCoords[bestMonitorIndex];
				y	-=yCoords[bestMonitorIndex];
			}

			//add desired monitor xy
			x	+=xCoords[monitorIndex];
			y	+=yCoords[monitorIndex];

			Console.WriteLine(Glfw.VersionString);

			Glfw.SetWindowPosition(mWnd, x, y);

			//create window surface
			IntPtr	surfaceHandle;

			Result	result	=(Result)Vulkan.CreateWindowSurface(
				ds.GetInstance().GetInstance().Handle, mWnd, IntPtr.Zero, out surfaceHandle);
			if(result != Result.Success)
			{
				ds.ErrorSpew("Window surface creation failed: " + result.ToString());
				Glfw.DestroyWindow(mWnd);
				Glfw.Terminate();
				return;
			}

			AllocationCallbacks?	superAnnoyingParameter	=null;

			mSurface	=new SurfaceKhr(ds.GetInstance().GetInstance(),
				ref superAnnoyingParameter, surfaceHandle.ToInt64());

			mLastFrame	=Stopwatch.GetTimestamp();
		}


		public void SetTicRate(long updateTic, long renderTic)
		{
			mUpdateTicMS	=updateTic;
			mRenderTicMS	=renderTic;
		}


		public SurfaceKhr GetSurface()
		{
			return	mSurface;
		}


		//return true to quit
		public bool GameLoop()
		{
			if(bDestroyed)
			{
				return	true;
			}
			Glfw.PollEvents();

			if(Glfw.WindowShouldClose(mWnd))
			{
				Misc.SafeInvoke(eWindowClosing, null);
				return	true;
			}

			long	now		=Stopwatch.GetTimestamp();
			long	delta	=now - mLastFrame;

			//deltatime in ms
			delta	/=(Stopwatch.Frequency / 1000);

			mRenderAccum	+=Math.Max(delta, 1);
			mGameAccum		+=Math.Max(delta, 1);

			if(mGameAccum > mUpdateTicMS)
			{
				Nullable<long>	deltaTime	=mGameAccum;
				Misc.SafeInvoke(eGameTic, deltaTime);
				mGameAccum	=0;
			}
			if(mRenderAccum > mRenderTicMS)
			{
				Nullable<long>	deltaTime	=mRenderAccum;
				Misc.SafeInvoke(eRenderTic, null);
				mRenderAccum	=0;
			}
			mLastFrame	=now;

			//sleep a bit if delta times are low
			if(delta < 5)
			{
				Thread.Sleep(5);
			}

			return	false;
		}


		public void AttachInput(InputLib.Input inp)
		{
			mAttachedInput	=inp;

			Glfw.SetKeyCallback(mWnd, KeyCB);
		}


		public void Destroy()
		{
			mSurface.Dispose();
			Glfw.DestroyWindow(mWnd);
			Glfw.Terminate();
			bDestroyed	=true;
		}


		void KeyCB(IntPtr window, Keys key, int scanCode,
			InputState state, ModifierKeys mods)
		{
/*			Console.WriteLine("wnd: " + window + ", key: " + key +
				", scanCode: " + scanCode + ", InputState: "
				+ state.ToString() + ", mods: " + mods.ToString());
*/
			mAttachedInput.ProcessKeyEvent(key, state, mods);
		}
	}
}