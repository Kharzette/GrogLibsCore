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
		//physical devices
		List<PhysicalDevice>	mPhysicals	=new List<PhysicalDevice>();

		//created logical devices
		Dictionary<string, Device>	mLogicals	=new Dictionary<string, Device>();

		//physical device data
		List<PhysicalDeviceProperties>	mDeviceProps			=new List<PhysicalDeviceProperties>();
		List<PhysicalDeviceFeatures>	mDeviceFeatures			=new List<PhysicalDeviceFeatures>();
		List<QueueFamilyProperties[]>	mDeviceQueueFamProps	=new List<QueueFamilyProperties[]>();

		//phys device key, value is indexes of queue families
		Dictionary<int, List<int>>	mGraphicsIndexes	=new Dictionary<int, List<int>>();
		Dictionary<int, List<int>>	mComputeIndexes		=new Dictionary<int, List<int>>();
		Dictionary<int, List<int>>	mTransferIndexes	=new Dictionary<int, List<int>>();
		Dictionary<int, List<int>>	mSparseIndexes		=new Dictionary<int, List<int>>();

		//presentation support keyed on device, indexed by queue family
		Dictionary<int, List<bool>>	mbPresentSupport		=new Dictionary<int, List<bool>>();

		//queue family limits keyed on device index
		Dictionary<int, List<int>>	mQueueLimits	=new Dictionary<int, List<int>>();

		//queue names
		Dictionary<string, Queue>	mQueueNames		=new Dictionary<string, Queue>();

		//command stuff
		Dictionary<string, CommandPool>	mCommandPools	=new Dictionary<string, CommandPool>();

		//swap chain stuff
		Extent2D		mSwapExtent;
		SwapchainKhr	mSwapChain;
		ImageView		[]mChainImageViews;

		internal event EventHandler	eErrorSpam;


		public Devices(Instance inst, SurfaceKhr surf)
		{
			ExaminePhysicalDevices(inst, surf);			
		}


		//temporary?
		public Device GetLogicalDevice(string name)
		{
			return	mLogicals[name];
		}


		internal ImageView	[]GetChainImageViews()
		{
			return	mChainImageViews;
		}


		public Extent2D GetChainExtent()
		{
			return	mSwapExtent;
		}


		public int GetNumPhysicalDevices()
		{
			return	mPhysicals.Count;
		}


		public PhysicalDeviceProperties GetPhysicalDeviceProps(int index)
		{
			return	mDeviceProps[index];
		}


		public PhysicalDeviceFeatures GetPhysicalDeviceFeatures(int index)
		{
			return	mDeviceFeatures[index];
		}


		public bool bSupportsPresent(int physIndex, int famIndex)
		{
			if(physIndex < 0 || physIndex >= mPhysicals.Count)
			{
				Misc.SafeInvoke(eErrorSpam, "Physical index out of range in bSupportsPresent()");
				return	false;
			}
			if(famIndex < 0 || famIndex >= mDeviceQueueFamProps[physIndex].Length)
			{
				Misc.SafeInvoke(eErrorSpam, "Bad queue family index: " + famIndex + " in bSupportsPresent()");
				return	false;
			}
			return	mbPresentSupport[physIndex][famIndex];
		}


		public List<int> GetFamilyIndexesFor(Queues qTypes, int physIndex)
		{
			switch(qTypes)
			{
				case	Queues.Compute:
					return	mComputeIndexes[physIndex];
				case	Queues.Graphics:
					return	mGraphicsIndexes[physIndex];
				case	Queues.SparseBinding:
					return	mSparseIndexes[physIndex];
				case	Queues.Transfer:
					return	mTransferIndexes[physIndex];
			}
			return	null;
		}


		public bool CreateLogicalDevice(string name, int physIndex,
			List<DeviceQueueCreateInfo> queues, List<string> extensions,
			Nullable<PhysicalDeviceFeatures> features)
		{
			if(physIndex < 0 || physIndex >= mPhysicals.Count)
			{
				Misc.SafeInvoke(eErrorSpam, "Physical index out of range in CreateLogicalDevice()");
				return	false;
			}

			if(mLogicals.ContainsKey(name))
			{
				Misc.SafeInvoke(eErrorSpam, "Logical device name: " + name + " already in use in CreateLogicalDevice()");
				return	false;
			}

			DeviceCreateInfo	dci	=new DeviceCreateInfo(queues.ToArray(), null);

			if(extensions != null && extensions.Count > 0)
			{
				dci.EnabledExtensionNames	=extensions.ToArray();
			}

			if(features != null)
			{
				dci.EnabledFeatures	=features.Value;
			}

			Device	d	=mPhysicals[physIndex].CreateDevice(dci);

			mLogicals.Add(name, d);

			return	true;
		}


		public bool SetQueueName(string logName, string qName,
			int	famIndex, int qIndex)
		{
			if(!mLogicals.ContainsKey(logName))
			{
				Misc.SafeInvoke(eErrorSpam, "Bad logical device name: " + logName + " in SetQueueName()");
				return	false;
			}

			if(mQueueNames.ContainsKey(qName))
			{
				Misc.SafeInvoke(eErrorSpam, "Queue name already in use: " + qName + " in SetQueueName()");
				return	false;
			}

			Device	dv	=mLogicals[logName];

			int	physIndex	=mPhysicals.IndexOf(dv.Parent);

			if(famIndex < 0 || famIndex >= mDeviceQueueFamProps[physIndex].Length)
			{
				Misc.SafeInvoke(eErrorSpam, "Bad queue family index: " + famIndex + " in SetQueueName()");
				return	false;
			}

			if(qIndex < 0 || qIndex >= mQueueLimits[physIndex][qIndex])
			{
				Misc.SafeInvoke(eErrorSpam, "Bad queue index: " + qIndex + " in SetQueueName()");
				return	false;
			}

			Queue	q	=dv.GetQueue(famIndex, qIndex);

			mQueueNames.Add(qName, q);

			//make a command pool for this queue
			CommandPoolCreateInfo	cpci	=new CommandPoolCreateInfo(
				famIndex, CommandPoolCreateFlags.None);
			mCommandPools.Add(qName, dv.CreateCommandPool(cpci));

			return	true;
		}


		public bool CreateSwapChain(string logName, SurfaceKhr surf,
			int extentX, int extentY)
		{
			Device			dv		=mLogicals[logName];
			PhysicalDevice	phys	=dv.Parent;

			SurfaceCapabilitiesKhr	surfCaps		=phys.GetSurfaceCapabilitiesKhr(surf);
			SurfaceFormatKhr		[]surfFormats	=phys.GetSurfaceFormatsKhr(surf);
			PresentModeKhr			[]presModes		=phys.GetSurfacePresentModesKhr(surf);

			if(surfFormats.Length <=0 || presModes.Length <= 0)
			{
				Misc.SafeInvoke(eErrorSpam, "Bad formats or pres modes...");
				return	false;
			}

			mSwapExtent	=new Extent2D(extentX, extentY);

			SwapchainCreateInfoKhr	scci	=new SwapchainCreateInfoKhr(
				surf, Format.B8G8R8A8UNorm, mSwapExtent,
				surfCaps.MinImageCount, ColorSpaceKhr.SRgbNonlinear, 1,
				ImageUsages.ColorAttachment);

			mSwapChain	=dv.CreateSwapchainKhr(scci);
			if(mSwapChain == null)
			{
				Misc.SafeInvoke(eErrorSpam, "Create swap chain failed...");
				return	false;
			}

			VulkanCore.Image	[]chainImages		=mSwapChain.GetImages();

			mChainImageViews	=new ImageView[chainImages.Length];

			for(int i=0;i < chainImages.Length;i++)
			{
				ImageSubresourceRange	isr	=new ImageSubresourceRange(
					ImageAspects.Color, 0, 1, 0, 1);

				ImageViewCreateInfo	ivci	=new ImageViewCreateInfo(
					mSwapChain.Format, isr);

				mChainImageViews[i]	=chainImages[i].CreateView(ivci);
			}
			return	true;
		}


		void ExaminePhysicalDevices(Instance inst, SurfaceKhr surf)
		{
			PhysicalDevice	[]devs	=inst.GetInstance().EnumeratePhysicalDevices();

			Debug.Assert(devs.Length > 0);

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
