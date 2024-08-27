using System.Reflection;
using CounterStrikeSharp.API.Core;

namespace Zenith
{
	public static class CallerIdentifier
	{
		private static readonly string CurrentPluginName = Assembly.GetExecutingAssembly().GetName().Name!;

		public static string GetCallingPluginName()
		{
			var stackTrace = new System.Diagnostics.StackTrace(true);
			string callingPlugin = CurrentPluginName;

			for (int i = 1; i < stackTrace.FrameCount; i++)
			{
				var assembly = stackTrace.GetFrame(i)?.GetMethod()?.DeclaringType?.Assembly;
				var assemblyName = assembly?.GetName().Name;

				if (assemblyName == "CounterStrikeSharp.API")
					break;

				if (assemblyName != CurrentPluginName && assemblyName != null)
				{
					callingPlugin = assemblyName;
					break;
				}
			}

			return callingPlugin;
		}
	}
}
