using System.Reflection;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace Zenith
{
	public sealed partial class Plugin : BasePlugin
	{
		private const string GeoLiteURL = "https://github.com/P3TERX/GeoLite.mmdb/releases/latest/download/GeoLite2-Country.mmdb";

		public void ValidateGeoLiteDatabase()
		{
			string destinationPath = Path.Combine(ModuleDirectory, "GeoLite2-Country.mmdb");
			if (File.Exists(destinationPath))
				return;

			Logger.LogInformation("Downloading GeoLite2-Country.mmdb due to missing file.");

			_ = Task.Run(async () =>
			{
				try
				{
					HttpClient httpClient = new();

					using (var response = await httpClient.GetAsync(GeoLiteURL))
					{
						response.EnsureSuccessStatusCode();

						using var fs = new FileStream(destinationPath, FileMode.CreateNew);
						await response.Content.CopyToAsync(fs);
					}

					Logger.LogInformation("Download completed successfully.");
					return;
				}
				catch (Exception ex)
				{
					Logger.LogError($"An error occurred while downloading the file: {ex.Message}");
					return;
				}
			});
		}
	}
}

public static class CallerIdentifier
{
	private static readonly string CurrentPluginName = Assembly.GetExecutingAssembly().GetName().Name!;
	private static readonly string[] BlockAssemblies = ["System.", "K4-ZenithAPI", "KitsuneMenu"];
	public static readonly List<string> ModuleList = [];

	public static string GetCallingPluginName()
	{
		var stackTrace = new System.Diagnostics.StackTrace(true);

		for (int i = 1; i < stackTrace.FrameCount; i++)
		{
			var assembly = stackTrace.GetFrame(i)?.GetMethod()?.DeclaringType?.Assembly;
			var assemblyName = assembly?.GetName().Name;

			if (assemblyName == "CounterStrikeSharp.API")
				break;

			if (assemblyName != CurrentPluginName && assemblyName != null && !BlockAssemblies.Any(assemblyName.StartsWith))
			{
				if (!ModuleList.Contains(assemblyName))
					ModuleList.Add(assemblyName);

				return assemblyName;
			}
		}

		return CurrentPluginName;
	}
}
