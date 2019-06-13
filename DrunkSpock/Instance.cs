using System;
using System.Collections.Generic;

using GLFW;
using VulkanCore;
using VulkanCore.Ext;
using VulkanCore.Khr;

using VER	=VulkanCore.Version;
using INST	=VulkanCore.Instance;


namespace DrunkSpock
{
	public class Instance
	{
		INST	mInstance;


		internal Instance(string appName, string engName,
			VER appVer, VER engVer, VER apiVer, bool bDebug)
		{
			string	vlName	="";
			if(bDebug)
			{
				if(!bCheckValidation(out vlName))
				{
					return;
				}
			}

			ApplicationInfo	ai		=new ApplicationInfo();
			ai.ApplicationName		=appName;
			ai.ApplicationVersion	=appVer;
			ai.EngineName			=engName;
			ai.EngineVersion		=engVer;
			ai.ApiVersion			=apiVer;

			InstanceCreateInfo	ici	=new InstanceCreateInfo();
			ici.Next				=IntPtr.Zero;
			ici.ApplicationInfo		=ai;

			List<string>	extensions	=new List<string>();

			extensions.AddRange(Vulkan.GetRequiredInstanceExtensions());
			if(bDebug)
			{
				extensions.Add(Constant.InstanceExtension.ExtDebugReport);
			}

			//get extension stuff from glfw
			ici.EnabledExtensionNames	=extensions.ToArray();

			if(bDebug)
			{
				ici.EnabledLayerNames	=new string[1] { vlName };
			}
			
			mInstance	=new INST(ici, null);
		}


		internal INST GetInstance()
		{
			return	mInstance;
		}


		internal bool IsValid()
		{
			return	(mInstance != null);
		}


		internal void Destroy()
		{
			mInstance.Dispose();
		}


		internal DebugReportCallbackExt AttachDebugCallback(DebugReportCallbackCreateInfoExt drcbci)
		{
			return	mInstance.CreateDebugReportCallbackExt(drcbci);
		}


		bool bCheckValidation(out string vlName)
		{
			LayerProperties	[]lps	=INST.EnumerateLayerProperties();

			bool	bFound	=false;
			vlName			="";
			foreach(LayerProperties lp in lps)
			{
				if(lp.Description.Contains("Standard Validation"))
				{
					bFound	=true;
					vlName	=lp.LayerName;
				}
			}

			if(!bFound)
			{
				Console.WriteLine("Missing standard validation layer...");
				return	false;
			}
			return	true;
		}
	}
}