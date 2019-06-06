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
	internal static class Shaders
	{
		internal static ShaderModule	LoadShader(string filePath, Device dv)
		{
			if(!File.Exists(filePath))
			{
				Console.WriteLine("Bad file path: " + filePath + " in LoadShader()");
				return	null;
			}
			FileStream		fs	=new FileStream(filePath, FileMode.Open, FileAccess.Read);
			if(fs == null)
			{
				Console.WriteLine("Couldn't open shader: " + filePath + " in LoadShader()");
				return	null;
			}
			BinaryReader	br	=new BinaryReader(fs);
			if(br == null)
			{
				Console.WriteLine("Couldn't open shader: " + filePath + " in LoadShader()");
				return	null;
			}

			byte	[]bytes	=br.ReadBytes((int)fs.Length);

			br.Close();
			fs.Close();

			ShaderModuleCreateInfo	smci	=new ShaderModuleCreateInfo(bytes);

			ShaderModule	sm	=dv.CreateShaderModule(smci);

			return	sm;
		}

	}
}