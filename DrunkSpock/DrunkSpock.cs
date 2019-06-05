using System;
using System.Collections.Generic;

using GLFW;
using VulkanCore;
using VulkanCore.Ext;
using VulkanCore.Khr;
using UtilityLib;

using VER	=VulkanCore.Version;


namespace DrunkSpock
{
    public class DrunkSpock
    {
		Instance	mInstance;

		DebugReportCallbackExt	mDebugCB;

		VER	mAPIVer		=new VER(1, 0, 0);
		VER	mEngineVer	=new VER(0, 4, 0);

		public event EventHandler	eErrorSpam;

		const string	EngineName		="GrogLibs";


		//temporary while converting from sample stuff
		public Instance	GetInstance()
		{
			return	mInstance;
		}

		public bool InitVulkan()
		{
			return	InitVulkan("A_GrogApp_00", VER.Zero, true);
		}


		public bool InitVulkan(string appName, VER appVer, bool bDebug)
		{
			Glfw.Init();

			mInstance	=new Instance(appName, EngineName,
				appVer, mEngineVer, mAPIVer, bDebug);

			if(!mInstance.IsValid())
			{
				Misc.SafeInvoke(eErrorSpam, "Couldn't create Vulkan instance.");
				return	false;
			}

			if(bDebug)
			{
				//set up debug callback to talk to validation stuff
				DebugReportCallbackCreateInfoExt	drcbci	=new DebugReportCallbackCreateInfoExt(
					DebugReportFlagsExt.All, DebugReportCallback, IntPtr.Zero);
				mDebugCB	=mInstance.AttachDebugCallback(drcbci);
			}

			return	true;
		}


		public void Destroy()
		{
			if(mDebugCB != null)
			{
				mDebugCB.Dispose();
			}

			if(mInstance != null)
			{
				if(mInstance.IsValid())
				{
					mInstance.Destroy();
				}
			}
		}


		internal void ErrorSpew(string err)
		{
			Misc.SafeInvoke(eErrorSpam, err);
		}


		bool DebugReportCallback(DebugReportCallbackInfo drci)
		{
			string	spew	=$"[{drci.Flags}][{drci.LayerPrefix}] {drci.Message}";

			Misc.SafeInvoke(eErrorSpam, spew);

			return	drci.Flags.HasFlag(DebugReportFlagsExt.Error);
		}



/*
			QueueFamilyProperties[]	qfps	=devs[0].GetQueueFamilyProperties();
			bool	bFound	=false;
			int		graphicsIndex	=0;
			for(int i=0;i < qfps.Length;i++)
			{
				if((qfps[i].QueueFlags & Queues.Graphics) != 0)
				{
					bFound			=true;
					graphicsIndex	=i;
					break;
				}
			}

			if(!bFound)
			{
				Console.WriteLine("Gobliny gpu graphics bit...");
				gameWnd.Destroy();
				vkInst.Dispose();
				return;
			}

		}*/
    }
}
