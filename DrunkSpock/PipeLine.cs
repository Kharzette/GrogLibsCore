using System;
using System.IO;
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
	public class PipeLine
	{
		Devices	mDevices;

		VulkanCore.Pipeline	mPipe;
		RenderPass			mRenderPass;
		PipelineLayout		mPipeLayout;

		//synchro
		List<Semaphore>	mImageAvail			=new List<Semaphore>();
		List<Semaphore>	mRenderFinished		=new List<Semaphore>();
		List<Fence>		mInFlight			=new List<Fence>();
		int				mCurrentFrame		=0;

		Framebuffer	[]mChainBuffers;

		Dictionary<string, ShaderModule>	mShaders	=new Dictionary<string, ShaderModule>();

		public event EventHandler	eUpdateMVP;

		const int	MaxFramesInFlight	=2;



		public PipeLine(Devices dvs)
		{
			mDevices	=dvs;
		}


		public bool LoadShader(string filePath)
		{
			ShaderModule	sm	=Shaders.LoadShader(filePath,
									mDevices.GetLogicalDevice());
			if(sm != null)
			{
				mShaders.Add(filePath, sm);
				return	true;
			}
			return	false;
		}


		public bool CreateFrameBuffer()
		{
			ImageView	[]chainImageViews	=mDevices.GetChainImageViews();

			mChainBuffers	=new Framebuffer[chainImageViews.Length];

			Extent2D	chainExtent	=mDevices.GetChainExtent();

			for(int i=0;i < chainImageViews.Length;i++)
			{
				mChainBuffers[i]	=mRenderPass.CreateFramebuffer(new FramebufferCreateInfo(
					new[] { chainImageViews[i] }, chainExtent.Width, chainExtent.Height, 1));
			}
			return	true;
		}


		public CommandBuffer[] GimmeCommandBuffers(string queueName)
		{
			CommandBufferAllocateInfo	cbai	=new CommandBufferAllocateInfo();

			cbai.Level				=CommandBufferLevel.Primary;
			cbai.CommandBufferCount	=mChainBuffers.Length;

			CommandPool	cp	=mDevices.GetCommandPool(queueName);

			return	cp.AllocateBuffers(cbai);
		}


		public void DestroySwapStuff()
		{
			for(int i=0;i < mChainBuffers.Length;i++)
			{
				if(mChainBuffers[i] != null)
				{
					mChainBuffers[i].Dispose();
				}
			}

			mPipe.Dispose();
			mRenderPass.Dispose();
		}


		public void Destroy()
		{
			DestroySwapStuff();

			foreach(Semaphore sem in mImageAvail)
			{
				sem.Dispose();
			}
			foreach(Semaphore sem in mRenderFinished)
			{
				sem.Dispose();
			}
			foreach(Fence f in mInFlight)
			{
				f.Dispose();
			}
			mImageAvail.Clear();
			mRenderFinished.Clear();
			mInFlight.Clear();
		}


		public void BeginBuffer(CommandBuffer cb,
				int frameBufIndex, ClearValue cv)
		{
			CommandBufferBeginInfo	cbbi	=new CommandBufferBeginInfo();

			cbbi.Flags	=CommandBufferUsages.SimultaneousUse;

			cb.Begin(cbbi);

			RenderPassBeginInfo	rpbi	=new RenderPassBeginInfo(
				mChainBuffers[frameBufIndex],
				new Rect2D(Offset2D.Zero, mDevices.GetChainExtent()), cv);

			//rpbi.ClearValues	=cv;

			cb.CmdBeginRenderPass(rpbi);

			cb.CmdBindPipeline(PipelineBindPoint.Graphics, mPipe);
		}


		public void DrawStuffs(CommandBuffer []cBufs,
			string graphicsQueueName, string presentQueueName)
		{
			int	imageIndex	=mDevices.AcquireNextImage(mImageAvail[mCurrentFrame]);

			Misc.SafeInvoke(eUpdateMVP, new Nullable<int>(imageIndex));

			SubmitInfo	si	=new SubmitInfo(
				new Semaphore[] { mImageAvail[mCurrentFrame] },
				new PipelineStages[] { PipelineStages.ColorAttachmentOutput },
				new CommandBuffer[] { cBufs[imageIndex] },
				new Semaphore[] { mRenderFinished[mCurrentFrame] });

			mDevices.GetLogicalDevice().ResetFences(
				new Fence[] { mInFlight[mCurrentFrame]});

			mDevices.SubmitToQueue(si, graphicsQueueName, mInFlight[mCurrentFrame]);

			mDevices.QueuePresent(mRenderFinished[mCurrentFrame], imageIndex, presentQueueName);

			mCurrentFrame	=(mCurrentFrame + 1) % MaxFramesInFlight;
		}


		bool CreateSyncObjects()
		{
			Device	dv	=mDevices.GetLogicalDevice();

			FenceCreateInfo	fci	=new FenceCreateInfo(FenceCreateFlags.Signaled);

			for(int i=0;i < MaxFramesInFlight;i++)
			{
				mImageAvail.Add(dv.CreateSemaphore());
				mRenderFinished.Add(dv.CreateSemaphore());
				mInFlight.Add(dv.CreateFence(fci));
			}
			return	true;
		}


		public void BindDS(CommandBuffer cb, int index)
		{
			cb.CmdBindDescriptorSets(PipelineBindPoint.Graphics, mPipeLayout,
				0, new DescriptorSet[] { mDevices.GetDescriptorSets(index) });
		}


		public bool Create(string vsName, string fsName,
			VertexInputBindingDescription []vbind,
			VertexInputAttributeDescription []vatts)
		{
			Device	dv	=mDevices.GetLogicalDevice();

			PipelineShaderStageCreateInfo	plssciv	=new PipelineShaderStageCreateInfo(
				ShaderStages.Vertex, mShaders[vsName], "main", null);

			PipelineShaderStageCreateInfo	plsscif	=new PipelineShaderStageCreateInfo(
				ShaderStages.Fragment, mShaders[fsName], "main", null);

			PipelineVertexInputStateCreateInfo	plvisci	=new PipelineVertexInputStateCreateInfo(
				vbind, vatts);

			PipelineInputAssemblyStateCreateInfo	pliasci	=new PipelineInputAssemblyStateCreateInfo(
				PrimitiveTopology.TriangleList);

			Viewport	vp	=new Viewport(0, 0, 1280, 720, 0f, 1f);

			Rect2D	scissor	=new Rect2D(Offset2D.Zero, mDevices.GetChainExtent());

			PipelineViewportStateCreateInfo	plvpsci	=new PipelineViewportStateCreateInfo(
				new Viewport[1] { vp }, new Rect2D[1] { scissor });
			
			PipelineRasterizationStateCreateInfo	plrsci	=new PipelineRasterizationStateCreateInfo();

			PipelineMultisampleStateCreateInfo	plmssci	=new PipelineMultisampleStateCreateInfo();

			PipelineColorBlendAttachmentState	plcbas	=new PipelineColorBlendAttachmentState();
			plcbas.ColorWriteMask	=ColorComponents.All;
			plcbas.BlendEnable		=false;

			PipelineColorBlendStateCreateInfo	plcbsci	=new PipelineColorBlendStateCreateInfo();
			plcbsci.LogicOpEnable	=false;
			plcbsci.LogicOp			=LogicOp.Copy;
			plcbsci.Attachments		=new PipelineColorBlendAttachmentState[1] { plcbas };
			plcbsci.BlendConstants	=ColorF4.Zero;

			PipelineLayoutCreateInfo	pllci	=new PipelineLayoutCreateInfo(
				mDevices.GetDSLs());

			mPipeLayout	=dv.CreatePipelineLayout(pllci);

			AttachmentDescription	ad	=new AttachmentDescription();

			ad.Format			=Format.B8G8R8A8UNorm;
			ad.Samples			=SampleCounts.Count1;
			ad.LoadOp			=AttachmentLoadOp.Clear;
			ad.StoreOp			=AttachmentStoreOp.Store;
			ad.StencilLoadOp	=AttachmentLoadOp.DontCare;
			ad.StencilStoreOp	=AttachmentStoreOp.DontCare;
			ad.InitialLayout	=ImageLayout.Undefined;
			ad.FinalLayout		=ImageLayout.PresentSrcKhr;

			AttachmentReference	ar	=new AttachmentReference(0, ImageLayout.ColorAttachmentOptimal);

			SubpassDescription	spd	=new SubpassDescription();
			spd.ColorAttachments	=new AttachmentReference[1] { ar };

			SubpassDependency	spdc	=new SubpassDependency();
			spdc.SrcSubpass		=Constant.SubpassExternal;
			spdc.DstSubpass		=0;
			spdc.SrcStageMask	=PipelineStages.ColorAttachmentOutput;
			spdc.SrcAccessMask	=0;
			spdc.DstStageMask	=PipelineStages.ColorAttachmentOutput;
			spdc.DstAccessMask	=Accesses.ColorAttachmentRead | Accesses.ColorAttachmentWrite;


			RenderPassCreateInfo	rpci	=new RenderPassCreateInfo(
				new SubpassDescription[1] { spd },
				new AttachmentDescription[1] { ad },
				new SubpassDependency[1] { spdc});

			mRenderPass	=dv.CreateRenderPass(rpci);

			GraphicsPipelineCreateInfo	gplci	=new GraphicsPipelineCreateInfo();

			gplci.Stages	=new PipelineShaderStageCreateInfo[2] { plssciv, plsscif };
			gplci.VertexInputState		=plvisci;
			gplci.InputAssemblyState	=pliasci;
			gplci.ViewportState			=plvpsci;
			gplci.RasterizationState	=plrsci;
			gplci.MultisampleState		=plmssci;
			gplci.DepthStencilState		=null;
			gplci.ColorBlendState		=plcbsci;
			gplci.DynamicState			=null;
			gplci.Layout				=mPipeLayout;
			gplci.RenderPass			=mRenderPass;
			gplci.Subpass				=0;
			gplci.BasePipelineHandle	=null;
			gplci.BasePipelineIndex		=-1;

			mPipe	=dv.CreateGraphicsPipeline(gplci);

			CreateSyncObjects();

			return	true;
		}
	}
}
