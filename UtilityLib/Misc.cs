using System;

namespace UtilityLib
{
	public static partial class Misc
	{
		public static void SafeInvoke(this EventHandler eh, object sender)
		{
			if(eh != null)
			{
				eh(sender, EventArgs.Empty);
			}
		}


		public static void SafeInvoke(this EventHandler eh, object sender, EventArgs ea)
		{
			if(eh != null)
			{
				eh(sender, ea);
			}
		}


		public static void SafeInvoke<T>(this EventHandler<T> eh, object sender, T ea) where T : EventArgs
		{
			if(eh != null)
			{
				eh(sender, ea);
			}
		}
	}
}
