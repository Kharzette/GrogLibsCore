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
		SurfaceKhr	mSurface;

		DebugReportCallbackExt	mDebugCB;

		VER	mAPIVer		=new VER(1, 0, 0);
		VER	mEngineVer	=new VER(0, 4, 0);

		public event EventHandler	eErrorSpam;

		const string	EngineName		="GrogLibs";


		internal Instance	GetInstance()
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


		internal SurfaceKhr GetSurface()
		{
			return	mSurface;
		}


		internal void CreateWindowSurface(Window wnd)
		{
			//create window surface
			IntPtr	surfaceHandle;

			Result	result	=(Result)Vulkan.CreateWindowSurface(
				mInstance.GetInstance().Handle, wnd,
				IntPtr.Zero, out surfaceHandle);

			if(result != Result.Success)
			{
				ErrorSpew("Window surface creation failed: " + result.ToString());
				return;
			}

			AllocationCallbacks?	superAnnoyingParameter	=null;

			mSurface	=new SurfaceKhr(mInstance.GetInstance(),
				ref superAnnoyingParameter, surfaceHandle.ToInt64());

		}


		public void Destroy()
		{
			if(mDebugCB != null)
			{
				mDebugCB.Dispose();
			}

			if(mSurface != null)
			{
				mSurface.Dispose();
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
    }
}
