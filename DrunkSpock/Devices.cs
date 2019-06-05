using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using UtilityLib;

using GLFW;
using VulkanCore;
using VulkanCore.Ext;
using VulkanCore.Khr;

using VER	=VulkanCore.Version;
using INST	=VulkanCore.Instance;


namespace DrunkSpock
{
	public class Devices
	{
		public enum QueueTypes
		{
			Compute,
			Graphics,
			SparseBinding,
			Transfer,
			Present,				//prefer queue just for present
			GraphicsPlusPresent		//prefer g & p combined
		};

		//physical devices
		List<PhysicalDevice>	mPhysicals	=new List<PhysicalDevice>();

		//created logical devices
		List<Device>	mLogicals	=new List<Device>();

		//physical device data
		List<PhysicalDeviceProperties>	mDeviceProps			=new List<PhysicalDeviceProperties>();
		List<PhysicalDeviceFeatures>	mDeviceFeatures			=new List<PhysicalDeviceFeatures>();
		List<QueueFamilyProperties[]>	mDeviceQueueFamProps	=new List<QueueFamilyProperties[]>();

		//device key, value is indexes of queue families
		Dictionary<int, List<int>>	mGraphicsIndexes	=new Dictionary<int, List<int>>();
		Dictionary<int, List<int>>	mComputeIndexes		=new Dictionary<int, List<int>>();
		Dictionary<int, List<int>>	mTransferIndexes	=new Dictionary<int, List<int>>();
		Dictionary<int, List<int>>	mSparseIndexes		=new Dictionary<int, List<int>>();

		//presentation support keyed on device, indexed by queue family
		Dictionary<int, List<bool>>	mbPresentSupport		=new Dictionary<int, List<bool>>();

		//queue family limits keyed on device index
		Dictionary<int, List<int>>	mQueueLimits	=new Dictionary<int, List<int>>();

		//queues associated with device
		Dictionary<Device, List<Queue>>	mQueues	=new Dictionary<Device, List<Queue>>();

		//queue names
		Dictionary<string, Queue>	mQueueNames		=new Dictionary<string, Queue>();

		//wanted queues to be made
		List<QueueTypes>	mWantQ			=new List<QueueTypes>();
		List<string>		mWantNames		=new List<string>();
		List<float>			mWantPriorities	=new List<float>();

		internal event EventHandler	eErrorSpam;


		public Devices(Instance inst, SurfaceKhr surf)
		{
			ExaminePhysicalDevices(inst, surf);			
		}


		int TotalWanted(QueueTypes kind)
		{
			int	total	=0;
			foreach(QueueTypes qt in mWantQ)
			{
				if(qt == kind)
				{
					total++;
				}
			}
			return	total;
		}


		int TotalPossible(QueueTypes kind)
		{
			int	total	=0;
			if(kind == QueueTypes.Compute)
			{
				foreach(KeyValuePair<int, List<int>> qfi in mComputeIndexes)
				{
					foreach(int idx in qfi.Value)
					{
						total	+=mQueueLimits[qfi.Key][idx];
					}
				}
			}
			else if(kind == QueueTypes.Graphics)
			{
				foreach(KeyValuePair<int, List<int>> qfi in mGraphicsIndexes)
				{
					foreach(int idx in qfi.Value)
					{
						total	+=mQueueLimits[qfi.Key][idx];
					}
				}
			}
			else if(kind == QueueTypes.GraphicsPlusPresent)			
			{
				foreach(KeyValuePair<int, List<int>> qfi in mGraphicsIndexes)
				{
					foreach(int idx in qfi.Value)
					{
						if(mbPresentSupport[qfi.Key][idx])
						{
							total	+=mQueueLimits[qfi.Key][idx];
						}
					}
				}
			}
			else if(kind == QueueTypes.Present)
			{
				foreach(KeyValuePair<int, List<bool>> qps in mbPresentSupport)
				{
					foreach(bool b in qps.Value)
					{
						if(b)
						{
							total++;
						}
					}
				}
			}
			else if(kind == QueueTypes.SparseBinding)
			{
				foreach(KeyValuePair<int, List<int>> qfi in mSparseIndexes)
				{
					foreach(int idx in qfi.Value)
					{
						total	+=mQueueLimits[qfi.Key][idx];
					}
				}
			}
			else if(kind == QueueTypes.Transfer)
			{
				foreach(KeyValuePair<int, List<int>> qfi in mTransferIndexes)
				{
					foreach(int idx in qfi.Value)
					{
						total	+=mQueueLimits[qfi.Key][idx];
					}
				}
			}
			return	total;
		}


		public bool WantQueue(QueueTypes kind, string name, float priority)
		{
			//make sure name is unique
			if(mWantNames.Contains(name))
			{
				Misc.SafeInvoke(eErrorSpam, "Name: " + name + " already in use!");
				return	false;
			}

			//make sure there's room for it
			if(TotalWanted(kind) < TotalPossible(kind))
			{
				mWantQ.Add(kind);
				mWantNames.Add(name);
				mWantPriorities.Add(priority);
				return	true;
			}
			return	false;
		}


		public void CreateSwapChain(int physIndex, SurfaceKhr surf)
		{
			PhysicalDevice	phys	=mPhysicals[physIndex];
			Device			dv		=mLogicals[physIndex];

			Debug.Assert(dv.Parent == phys);

			SurfaceCapabilitiesKhr	surfCaps		=phys.GetSurfaceCapabilitiesKhr(surf);
			SurfaceFormatKhr		[]surfFormats	=phys.GetSurfaceFormatsKhr(surf);
			PresentModeKhr			[]presModes		=phys.GetSurfacePresentModesKhr(surf);

			if(surfFormats.Length <=0 || presModes.Length <= 0)
			{
				Misc.SafeInvoke(eErrorSpam, "Bad formats or pres modes...");
				return;
			}

			Extent2D	chainExtent	=new Extent2D(1280, 720);

			SwapchainCreateInfoKhr	scci	=new SwapchainCreateInfoKhr(
				surf, Format.B8G8R8A8UNorm, chainExtent,
				surfCaps.MinImageCount, ColorSpaceKhr.SRgbNonlinear, 1,
				ImageUsages.ColorAttachment);

			SwapchainKhr	swapChain	=dv.CreateSwapchainKhr(scci);

			VulkanCore.Image	[]chainImages		=swapChain.GetImages();
			ImageView			[]chainImageViews	=new ImageView[chainImages.Length];

			for(int i=0;i < chainImages.Length;i++)
			{
				ImageSubresourceRange	isr	=new ImageSubresourceRange(
					ImageAspects.Color, 0, 1, 0, 1);

				ImageViewCreateInfo	ivci	=new ImageViewCreateInfo(
					swapChain.Format, isr);

				chainImageViews[i]	=chainImages[i].CreateView(ivci);
			}

		}


		public void CreateLogicals()
		{
			//total up the queues wanted
			int	computeWanted		=mWantQ.Count(q => q == QueueTypes.Compute);
			int	graphicsWanted		=mWantQ.Count(q => q == QueueTypes.Graphics);
			int	graPlusPresWanted	=mWantQ.Count(q => q == QueueTypes.GraphicsPlusPresent);
			int	presWanted			=mWantQ.Count(q => q == QueueTypes.Present);
			int	sparseWanted		=mWantQ.Count(q => q == QueueTypes.SparseBinding);
			int	transWanted			=mWantQ.Count(q => q == QueueTypes.Transfer);

			int	totalWanted	=computeWanted + graphicsWanted + graPlusPresWanted +
								presWanted + sparseWanted + transWanted;

			//track family spots left
			Dictionary<int, List<int>>	famQueueRemaining	=
				new Dictionary<int, List<int>>(mQueueLimits);

			List<DeviceQueueCreateInfo>	qci	=new List<DeviceQueueCreateInfo>();
			List<int>					qd	=new List<int>();

			for(int i=0;i < mWantQ.Count;i++)
			{
				//find a queue spot
				int	dev			=-1;
				int	famIndex	=-1;

				Dictionary<int, List<int>>	indexer;
				switch(mWantQ[i])
				{
					case	QueueTypes.Compute:
						indexer	=mComputeIndexes;
						break;
					case	QueueTypes.Graphics:
						indexer	=mGraphicsIndexes;
						break;
					case	QueueTypes.GraphicsPlusPresent:
						indexer	=mGraphicsIndexes;
						break;
					case	QueueTypes.Present:
						indexer	=mGraphicsIndexes;	//present queues always graphics?
						break;
					case	QueueTypes.SparseBinding:
						indexer	=mSparseIndexes;
						break;
					case	QueueTypes.Transfer:
						indexer	=mTransferIndexes;
						break;
					default:
						indexer	=mTransferIndexes;	//?
						break;
				}
				foreach(KeyValuePair<int, List<int>> ci in indexer)
				{
					foreach(int idx in ci.Value)
					{
						if(famQueueRemaining[ci.Key][idx] > 0)
						{
							dev			=ci.Key;
							famIndex	=idx;
							break;
						}
					}
					if(dev != -1)
					{
						break;
					}
				}
				DeviceQueueCreateInfo	dqci	=new DeviceQueueCreateInfo(
					famIndex, 1, new float[] { mWantPriorities[i] });
				qci.Add(dqci);
				qd.Add(dev);
			}

			//split by device
			for(int dev=0;dev < mPhysicals.Count;dev++)
			{
				List<DeviceQueueCreateInfo>	perDev	=new List<DeviceQueueCreateInfo>();

				bool	bAnyPresent	=false;
				for(int i=0;i < qci.Count;i++)
				{
					if(qd[dev] == dev)
					{
						perDev.Add(qci[i]);

						if(mWantQ[i] == QueueTypes.GraphicsPlusPresent
							|| mWantQ[i] == QueueTypes.Present)
						{
							bAnyPresent	=true;							
						}
					}
				}

				DeviceCreateInfo	dci	=new DeviceCreateInfo(perDev.ToArray(), null);

				if(bAnyPresent)
				{
					dci.EnabledExtensionNames	=new string[1] { "VK_KHR_swapchain" };
				}
				Device	d	=mPhysicals[dev].CreateDevice(dci);

				mLogicals.Add(d);
				mQueues.Add(d, new List<Queue>());

				//count up families in use
				Dictionary<int, int>	famsInUse	=new Dictionary<int, int>();

				for(int i=0;i < perDev.Count;i++)
				{
					int	qfi	=perDev[i].QueueFamilyIndex;
					if(famsInUse.ContainsKey(qfi))
					{
						famsInUse[qfi]++;
					}
					else
					{
						famsInUse.Add(qfi, 1);
					}
				}

				//track next index
				Dictionary<int, int>	nextIndex	=new Dictionary<int, int>();
				foreach(KeyValuePair<int, int> fiu in famsInUse)
				{
					nextIndex.Add(fiu.Key, 0);
				}

				//grab queues
				for(int i=0;i < perDev.Count;i++)
				{
					int	ogIdx	=qci.IndexOf(perDev[i]);
					int	qfIdx	=perDev[i].QueueFamilyIndex;

					Queue	q	=d.GetQueue(qfIdx, nextIndex[qfIdx]);

					nextIndex[qfIdx]++;

					//add by name
					mQueueNames.Add(mWantNames[ogIdx], q);

					//associate with device
					mQueues[d].Add(q);
				}
			}
		}


		void ExaminePhysicalDevices(Instance inst, SurfaceKhr surf)
		{
			PhysicalDevice	[]devs	=inst.GetInstance().EnumeratePhysicalDevices();
			if(devs.Length <= 0)
			{
//				ErrorSpew("No physical devices returned from Enumeration.");
//				Destroy();
				return;
			}

			mPhysicals.AddRange(devs);

			mDeviceProps.Clear();
			mDeviceFeatures.Clear();
			mDeviceQueueFamProps.Clear();

			foreach(PhysicalDevice pd in devs)
			{
				mDeviceProps.Add(pd.GetProperties());
				mDeviceFeatures.Add(pd.GetFeatures());
				mDeviceQueueFamProps.Add(pd.GetQueueFamilyProperties());
			}

			for(int dev=0;dev < mDeviceQueueFamProps.Count;dev++)
			{
				mComputeIndexes.Add(dev, new List<int>());
				mGraphicsIndexes.Add(dev, new List<int>());
				mSparseIndexes.Add(dev, new List<int>());
				mTransferIndexes.Add(dev, new List<int>());
				mQueueLimits.Add(dev, new List<int>());
				mbPresentSupport.Add(dev, new List<bool>());
				
				QueueFamilyProperties	[]qfps	=mDeviceQueueFamProps[dev];
				for(int i=0;i < qfps.Length;i++)
				{
					mQueueLimits[dev].Add(qfps[i].QueueCount);
					mbPresentSupport[dev].Add(
						devs[dev].GetSurfaceSupportKhr(i, surf));

					if((qfps[i].QueueFlags & Queues.Compute) != 0)
					{
						mComputeIndexes[dev].Add(i);
					}
					if((qfps[i].QueueFlags & Queues.Graphics) != 0)
					{
						mGraphicsIndexes[dev].Add(i);
					}
					if((qfps[i].QueueFlags & Queues.SparseBinding) != 0)
					{
						mSparseIndexes[dev].Add(i);
					}
					if((qfps[i].QueueFlags & Queues.Transfer) != 0)
					{
						mTransferIndexes[dev].Add(i);
					}
				}
			}
		}
	}
}
