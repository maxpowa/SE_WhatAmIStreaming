using HarmonyLib;
using Microsoft.Extensions.DependencyInjection;
using VRage.Plugins;

namespace SE_WhatAmIStreaming;

public class Plugin : IPlugin, IDisposable
{
    private static Harmony _harmony;
    private const string Name = "What Am I Streaming?";

    public void Init(object gameInstance)
    {
        _harmony = new Harmony(Name);
        _harmony.PatchAll();
    }

    public void Update()
    {
    }

    public void Dispose()
    {
        _harmony?.UnpatchAll(Name);
        _harmony = null;
    }

    public static void RegisterServices(IServiceCollection services)
    {
    }
}
