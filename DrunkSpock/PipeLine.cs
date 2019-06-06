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
		string	mDeviceName;

		VulkanCore.Pipeline	mPipe;
		RenderPass			mRenderPass;

		Semaphore	mImageAvail;
		Semaphore	mRenderFinished;

		Framebuffer	[]mChainBuffers;

		Dictionary<string, ShaderModule>	mShaders	=new Dictionary<string, ShaderModule>();



		public PipeLine(Devices dvs, string deviceName)
		{
			mDevices	=dvs;
			mDeviceName	=deviceName;
		}


		public bool LoadShader(string filePath)
		{
			Device	dv	=mDevices.GetLogicalDevice(mDeviceName);
			ShaderModule	sm	=Shaders.LoadShader(filePath, dv);
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


		public CommandBuffer[] GimmeCommandBuffer(string queueName)
		{
			CommandBufferAllocateInfo	cbai	=new CommandBufferAllocateInfo();

			cbai.Level				=CommandBufferLevel.Primary;
			cbai.CommandBufferCount	=mChainBuffers.Length;

			CommandPool	cp	=mDevices.GetCommandPool(queueName);

			return	cp.AllocateBuffers(cbai);
		}


		public void Destroy()
		{
			for(int i=0;i < mChainBuffers.Length;i++)
			{
				if(mChainBuffers[i] != null)
				{
					mChainBuffers[i].Dispose();
				}
			}
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
			int	imageIndex	=mDevices.AcquireNextImage(mImageAvail);

			SubmitInfo	si	=new SubmitInfo();
			si.WaitSemaphores	=new long[1] { mImageAvail.Handle };
			si.WaitDstStageMask	=new PipelineStages[1] { PipelineStages.ColorAttachmentOutput };
			si.CommandBuffers	=new IntPtr[1] { cBufs[imageIndex] };
			si.SignalSemaphores	=new long[1] { mRenderFinished.Handle };

			mDevices.SubmitToQueue(si, graphicsQueueName);

			mDevices.QueuePresent(mRenderFinished, imageIndex, presentQueueName);
		}


		public bool Create(string vsName, string fsName)
		{
			Device	dv	=mDevices.GetLogicalDevice(mDeviceName);

			PipelineShaderStageCreateInfo	plssciv	=new PipelineShaderStageCreateInfo(
				ShaderStages.Vertex, mShaders[vsName], "main", null);

			PipelineShaderStageCreateInfo	plsscif	=new PipelineShaderStageCreateInfo(
				ShaderStages.Fragment, mShaders[fsName], "main", null);

			PipelineVertexInputStateCreateInfo	plvisci	=new PipelineVertexInputStateCreateInfo(
				null, null);

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

			PipelineLayoutCreateInfo	pllci	=new PipelineLayoutCreateInfo();

			PipelineLayout	pll	=dv.CreatePipelineLayout(pllci);

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
			gplci.Layout				=pll;
			gplci.RenderPass			=mRenderPass;
			gplci.Subpass				=0;
			gplci.BasePipelineHandle	=null;
			gplci.BasePipelineIndex		=-1;

			mPipe	=dv.CreateGraphicsPipeline(gplci);

			mImageAvail		=dv.CreateSemaphore();
			mRenderFinished	=dv.CreateSemaphore();

			return	true;
		}
	}
}
