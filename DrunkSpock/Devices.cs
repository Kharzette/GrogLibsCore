using System;
using System.Linq;
using System.Diagnostics;
using System.Collections.Generic;
using UtilityLib;

using GLFW;
using VulkanCore;
using VulkanCore.Ext;
using VulkanCore.Khr;

using VER		=VulkanCore.Version;
using INST		=VulkanCore.Instance;
using Buffer	=VulkanCore.Buffer;


namespace DrunkSpock
{
	public class Devices
	{
		//physical devices
		List<PhysicalDevice>	mPhysicals	=new List<PhysicalDevice>();

		//created logical device
		Device	mLogical;

		//bufferstuff
		Dictionary<string, Buffer>			mBuffers	=new Dictionary<string, VulkanCore.Buffer>();
		Dictionary<string, DeviceMemory>	mDeviceMems	=new Dictionary<string, DeviceMemory>();

		DescriptorSetLayout	[]mDSLs;
		DescriptorPool		mDPool;
		DescriptorSet		[]mDSets;

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

		//command stuff keyed on queue name
		Dictionary<string, CommandPool>	mCommandPools	=new Dictionary<string, CommandPool>();

		//swap chain stuff
		Extent2D		mSwapExtent;
		SwapchainKhr	mSwapChain;
		ImageView		[]mChainImageViews;

		internal event	EventHandler	eErrorSpam;
		public event	EventHandler	eSwapChainOutOfDate;


		public Devices(DrunkSpock spock)
		{
			ExaminePhysicalDevices(spock.GetInstance(), spock.GetSurface());			
		}


		internal CommandPool GetCommandPool(string queueName)
		{
			return	mCommandPools[queueName];
		}


		internal int AcquireNextImage(Semaphore sem)
		{
			int	imageIndex	=mSwapChain.AcquireNextImage(-1, sem);

			return	imageIndex;
		}


		internal void QueuePresent(Semaphore sem,
				int imageIndex, string presQueueName)
		{
			PresentInfoKhr	pik	=new PresentInfoKhr(
				new Semaphore[] { sem },
				new SwapchainKhr[] { mSwapChain },
				new int[1] { imageIndex });

			try
			{
				mQueueNames[presQueueName].PresentKhr(pik);
			}
			catch(VulkanException ex)
			{
				if(ex.Result == Result.ErrorOutOfDateKhr)
				{
					Misc.SafeInvoke(eSwapChainOutOfDate, null);
				}
			}
		}


		public void DestroyBuffer(string name)
		{
			if(!mBuffers.ContainsKey(name))
			{
				return;
			}

			mBuffers[name].Dispose();
			mBuffers.Remove(name);
		}


		public void DestroySwapChainStuff()
		{
			foreach(ImageView iv in mChainImageViews)
			{
				iv.Dispose();
			}
			mChainImageViews	=null;

			mDPool.Dispose();

			mSwapChain.Dispose();
		}


		public void Destroy()
		{
			DestroySwapChainStuff();

			foreach(KeyValuePair<string, Buffer> bufs in mBuffers)
			{
				bufs.Value.Dispose();
			}
			mBuffers.Clear();

			foreach(KeyValuePair<string, DeviceMemory> mem in mDeviceMems)
			{
				mem.Value.Dispose();
			}
			mDeviceMems.Clear();

			foreach(KeyValuePair<string, CommandPool> cp in mCommandPools)
			{
				cp.Value.Dispose();
			}
			mCommandPools.Clear();

			foreach(DescriptorSetLayout dsl in mDSLs)
			{
				dsl.Dispose();
			}
			mDSLs	=null;

			//queues are cleaned up automagically
			mLogical.Dispose();
		}


		internal void SubmitToQueue(SubmitInfo si,
			string queueName, Fence fence = null)
		{
			SubmitToQueue(new SubmitInfo[] { si }, queueName, fence);
		}


		public void WaitIdle()
		{
			mLogical.WaitIdle();
		}


		internal void SubmitToQueue(SubmitInfo []sis,
			string queueName, Fence fence = null)
		{
			mQueueNames[queueName].Submit(sis, fence);
		}


		internal Device GetLogicalDevice()
		{
			return	mLogical;
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


		bool bQueueIndexIn(Queues qType, int physIndex, int theIndex)
		{
			if(qType == Queues.Compute)
			{
				if(mComputeIndexes.ContainsKey(physIndex))
				{
					return	mComputeIndexes[physIndex].Contains(theIndex);
				}
			}
			if(qType == Queues.Graphics)
			{
				if(mGraphicsIndexes.ContainsKey(physIndex))
				{
					return	mGraphicsIndexes[physIndex].Contains(theIndex);
				}
			}
			if(qType == Queues.SparseBinding)
			{
				if(mSparseIndexes.ContainsKey(physIndex))
				{
					return	mSparseIndexes[physIndex].Contains(theIndex);
				}
			}
			if(qType == Queues.Transfer)
			{
				if(mTransferIndexes.ContainsKey(physIndex))
				{
					return	mTransferIndexes[physIndex].Contains(theIndex);
				}
			}
			return	false;
		}


		public int GetExclusiveFamilyIndex(Queues qType, int physIndex)
		{
			Dictionary<int, List<int>>	qIndexes	=null;

			List<Queues>	otherThree	=new List<Queues>();

			if(qType == Queues.Compute)
			{
				qIndexes	=mComputeIndexes;
				otherThree.Add(Queues.Graphics);
				otherThree.Add(Queues.SparseBinding);
				otherThree.Add(Queues.Transfer);
			}
			else if(qType == Queues.Graphics)
			{
				qIndexes	=mGraphicsIndexes;
				otherThree.Add(Queues.Compute);
				otherThree.Add(Queues.SparseBinding);
				otherThree.Add(Queues.Transfer);
			}
			else if(qType == Queues.SparseBinding)
			{
				qIndexes	=mSparseIndexes;
				otherThree.Add(Queues.Compute);
				otherThree.Add(Queues.Graphics);
				otherThree.Add(Queues.Transfer);
			}
			else if(qType == Queues.Transfer)
			{
				qIndexes	=mTransferIndexes;
				otherThree.Add(Queues.Compute);
				otherThree.Add(Queues.SparseBinding);
				otherThree.Add(Queues.Graphics);
			}

			if(!qIndexes.ContainsKey(physIndex))
			{
				return	-1;
			}

			foreach(int i in qIndexes[physIndex])
			{
				if(bQueueIndexIn(otherThree[0], physIndex, i))
				{
					continue;
				}
				else if(bQueueIndexIn(otherThree[1], physIndex, i))
				{
					continue;
				}
				else if(bQueueIndexIn(otherThree[2], physIndex, i))
				{
					continue;
				}
				return	i;
			}
			return	-1;
		}


		public List<int> GetFamilyIndexesFor(Queues qTypes, int physIndex)
		{
			if(qTypes == Queues.Compute)
			{
				if(mComputeIndexes.ContainsKey(physIndex))
				{
					return	mComputeIndexes[physIndex];
				}
			}
			if(qTypes == Queues.Graphics)
			{
				if(mGraphicsIndexes.ContainsKey(physIndex))
				{
					return	mGraphicsIndexes[physIndex];
				}
			}
			if(qTypes == Queues.SparseBinding)
			{
				if(mSparseIndexes.ContainsKey(physIndex))
				{
					return	mSparseIndexes[physIndex];
				}
			}
			if(qTypes == Queues.Transfer)
			{
				if(mTransferIndexes.ContainsKey(physIndex))
				{
					return	mTransferIndexes[physIndex];
				}
			}

			return	null;
		}


		public void CopyBuffer(string queue,
			string src, string dest, int size)
		{
			if(!mBuffers.ContainsKey(src))
			{
				Misc.SafeInvoke(eErrorSpam, "No buffer named: " + src + "...");
				return;
			}
			if(!mBuffers.ContainsKey(dest))
			{
				Misc.SafeInvoke(eErrorSpam, "No buffer named: " + dest + "...");
				return;
			}
			if(!mCommandPools.ContainsKey(queue))
			{
				Misc.SafeInvoke(eErrorSpam, "No pool named: " + queue + "...");
				return;
			}

			CommandBufferAllocateInfo	cbai	=new CommandBufferAllocateInfo();

			cbai.Level				=CommandBufferLevel.Primary;
			cbai.CommandBufferCount	=1;

			CommandBuffer	[]bufs	=mCommandPools[queue].AllocateBuffers(cbai);

			CommandBufferBeginInfo	cbbi	=new CommandBufferBeginInfo(
				CommandBufferUsages.OneTimeSubmit);

			bufs[0].Begin(cbbi);

			BufferCopy	bc	=new BufferCopy(size, 0, 0);

			bufs[0].CmdCopyBuffer(mBuffers[src], mBuffers[dest], bc);

			bufs[0].End();

			SubmitInfo	si	=new SubmitInfo();

			si.CommandBuffers	=new IntPtr[] { bufs[0].Handle };

			SubmitToQueue(si, queue, null);

			mQueueNames[queue].WaitIdle();

			bufs[0].Dispose();
		}

		
		public bool CreateLogicalDevice(int physIndex,
			List<DeviceQueueCreateInfo> queues,
			List<string> extensions,
			Nullable<PhysicalDeviceFeatures> features)
		{
			if(physIndex < 0 || physIndex >= mPhysicals.Count)
			{
				Misc.SafeInvoke(eErrorSpam, "Physical index out of range in CreateLogicalDevice()");
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

			mLogical	=mPhysicals[physIndex].CreateDevice(dci);

			return	true;
		}


		public bool SetQueueName(string qName,
			int	famIndex, int qIndex, CommandPoolCreateFlags poolFlags)
		{
			if(mQueueNames.ContainsKey(qName))
			{
				Misc.SafeInvoke(eErrorSpam, "Queue name already in use: " + qName + " in SetQueueName()");
				return	false;
			}

			int	physIndex	=mPhysicals.IndexOf(mLogical.Parent);

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

			Queue	q	=mLogical.GetQueue(famIndex, qIndex);

			mQueueNames.Add(qName, q);

			//make a command pool for this queue
			CommandPoolCreateInfo	cpci	=new CommandPoolCreateInfo(
				famIndex, poolFlags);
			mCommandPools.Add(qName, mLogical.CreateCommandPool(cpci));

			return	true;
		}


		public bool CreateSwapChain(DrunkSpock spock,
			int extentX, int extentY)
		{
			PhysicalDevice	phys	=mLogical.Parent;
			SurfaceKhr		surf	=spock.GetSurface();

			SurfaceCapabilitiesKhr	surfCaps		=phys.GetSurfaceCapabilitiesKhr(surf);
			SurfaceFormatKhr		[]surfFormats	=phys.GetSurfaceFormatsKhr(surf);
			PresentModeKhr			[]presModes		=phys.GetSurfacePresentModesKhr(surf);

			if(surfFormats.Length <=0 || presModes.Length <= 0)
			{
				Misc.SafeInvoke(eErrorSpam, "Bad formats or pres modes...");
				return	false;
			}

			mSwapExtent	=new Extent2D(extentX, extentY);

			int	imageCount	=surfCaps.MinImageCount + 1;
			if(surfCaps.MaxImageCount > 0 && imageCount > surfCaps.MaxImageCount)
			{
				imageCount	=surfCaps.MaxImageCount;
			}

			SwapchainCreateInfoKhr	scci	=new SwapchainCreateInfoKhr(
				surf, Format.B8G8R8A8UNorm, mSwapExtent,
				imageCount, ColorSpaceKhr.SRgbNonlinear, 1,
				ImageUsages.ColorAttachment);

			scci.ImageSharingMode	=SharingMode.Exclusive;

			if(presModes.Contains(PresentModeKhr.Mailbox))
			{
				scci.PresentMode	=PresentModeKhr.Mailbox;
			}

			mSwapChain	=mLogical.CreateSwapchainKhr(scci);
			if(mSwapChain == null)
			{
				Misc.SafeInvoke(eErrorSpam, "Create swap chain failed...");
				return	false;
			}

			VulkanCore.Image	[]chainImages	=mSwapChain.GetImages();

			mChainImageViews	=new ImageView[chainImages.Length];

			for(int i=0;i < chainImages.Length;i++)
			{
				ImageSubresourceRange	isr	=new ImageSubresourceRange(
					ImageAspects.Color, 0, 1, 0, 1);

				ImageViewCreateInfo	ivci	=new ImageViewCreateInfo(
					mSwapChain.Format, isr);

				mChainImageViews[i]	=chainImages[i].CreateView(ivci);
			}

			//descriptor pool stuff
			DescriptorPoolSize	dps	=new DescriptorPoolSize(
				DescriptorType.UniformBuffer, chainImages.Length);
			
			DescriptorPoolCreateInfo	dpci	=new DescriptorPoolCreateInfo();

			dpci.PoolSizes	=new DescriptorPoolSize[] { dps };
			dpci.MaxSets	=chainImages.Length;

			mDPool	=mLogical.CreateDescriptorPool(dpci);

			return	true;
		}


		public void CreateBuffer(int sizeInBytes, string name,
			BufferUsages usage, SharingMode sm, int []famIndexes = null)
		{
			BufferCreateInfo	bci	=new BufferCreateInfo(sizeInBytes,
				usage, BufferCreateFlags.None,
				sm, famIndexes);

			Buffer	buf	=mLogical.CreateBuffer(bci);

			mBuffers.Add(name, buf);
		}


		public void CreateBufferMemory<T>(string bufName, MemoryProperties props)
		{
			if(mDeviceMems.ContainsKey(bufName))
			{
				Misc.SafeInvoke(eErrorSpam, "Buffer " + bufName + " already has a chunk of mem allocated...");
				return;
			}
			Buffer	buf	=mBuffers[bufName];

			DeviceMemory	dm;
			MemoryRequirements	mr	=buf.GetMemoryRequirements();

			MemoryAllocateInfo	mai	=new MemoryAllocateInfo();
			mai.AllocationSize		=mr.Size;
			mai.MemoryTypeIndex		=FindMemoryType(mr.MemoryTypeBits, props);

			dm	=mLogical.AllocateMemory(mai);

			mDeviceMems.Add(bufName, dm);
		}


		public void CopyStuffIntoMemory<T>(string memName, T[] stuff) where T : struct
		{
			if(!mDeviceMems.ContainsKey(memName))
			{
				Misc.SafeInvoke(eErrorSpam, "No memory chunk " + memName + "...");
				return;
			}

			DeviceMemory	dm	=mDeviceMems[memName];

            long	size	=stuff.Length * Interop.SizeOf<T>();

			IntPtr	pVBMem	=dm.Map(0, size);

			Interop.Write(pVBMem, stuff);

			dm.Unmap();
		}


		public void BindMemoryToBuffer(string memName, string bufName)
		{
			if(!mDeviceMems.ContainsKey(memName))
			{
				Misc.SafeInvoke(eErrorSpam, "No memory chunk " + memName + "...");
				return;
			}
			if(!mBuffers.ContainsKey(memName))
			{
				Misc.SafeInvoke(eErrorSpam, "No buffer " + bufName + "...");
				return;
			}

			Buffer			buf	=mBuffers[bufName];
			DeviceMemory	dm	=mDeviceMems[bufName];

			buf.BindMemory(dm);
		}


		public DescriptorSetLayout	[]GetDSLs()
		{
			return	mDSLs;
		}


		public void CreateDescriptorLayout()
		{
			DescriptorSetLayoutBinding	dslb	=new DescriptorSetLayoutBinding();

			dslb.Binding			=0;
			dslb.DescriptorType		=DescriptorType.UniformBuffer;
			dslb.DescriptorCount	=1;
			dslb.StageFlags			=ShaderStages.Vertex;

			DescriptorSetLayoutCreateInfo	dslci	=new DescriptorSetLayoutCreateInfo();

			dslci.Bindings	=new DescriptorSetLayoutBinding[] { dslb };

			mDSLs	=new DescriptorSetLayout[mChainImageViews.Length];

			for(int i=0;i < mChainImageViews.Length;i++)
			{
				mDSLs[i]	=mLogical.CreateDescriptorSetLayout(dslci);
			}
		}


		public void CreateDesriptorSets(string bufName, int size)
		{
			DescriptorSetAllocateInfo	dsai	=new DescriptorSetAllocateInfo(
				mChainImageViews.Length, mDSLs);

			mDSets	=mDPool.AllocateSets(dsai);

			for(int i=0;i < mChainImageViews.Length;i++)
			{
				DescriptorBufferInfo	dbi	=new DescriptorBufferInfo();

				dbi.Buffer	=mBuffers[bufName + i];
				dbi.Range	=size;

				WriteDescriptorSet	wds	=new WriteDescriptorSet();

				wds.DstSet			=mDSets[i];
				wds.DescriptorType	=DescriptorType.UniformBuffer;
				wds.DescriptorCount	=1;
				wds.BufferInfo		=new DescriptorBufferInfo[] { dbi };

				mDPool.UpdateSets(new WriteDescriptorSet[] { wds });
			}
		}


		internal DescriptorSet GetDescriptorSets(int index)
		{
			return	mDSets[index];
		}


		public void BindVB(CommandBuffer cb, string name)
		{
			cb.CmdBindVertexBuffer(mBuffers[name]);
		}


		public void BindIB(CommandBuffer cb, string name)
		{
			cb.CmdBindIndexBuffer(mBuffers[name], 0, IndexType.UInt16);
		}


		//from the vulkan tutorial
		int	FindMemoryType(int typeFilter, MemoryProperties memProps)
		{
			PhysicalDeviceMemoryProperties	pdmp	=mLogical.Parent.GetMemoryProperties();

			return	pdmp.MemoryTypes.IndexOf(typeFilter, memProps);
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
