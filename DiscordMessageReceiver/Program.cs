﻿using Microsoft.Extensions.Caching.Memory;

using Discord.WebSocket;
using Discord.Commands;

using DotNetEnv;

using Azure.Identity;

using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using Telemetry.Trace;

using DiscordMessageReceiver.Clients;
using DiscordMessageReceiver.Services;
using DiscordMessageReceiver.Services.Messengers;

Env.Load();

var builder = WebApplication.CreateBuilder(args);

var keyVaultUri = new Uri("https://vbn930-rpg-kv.vault.azure.net/");

builder.Configuration.AddAzureKeyVault(
    keyVaultUri,
    new DefaultAzureCredential());

IConfiguration configuration = builder.Configuration;

string? token = configuration["discord-bot-token"];
string? gameServiceBaseUrl = Environment.GetEnvironmentVariable("GAME_SERVICE_BASE_URL");

string serviceName = configuration["Logging:ServiceName"];
string serviceVersion = configuration["Logging:ServiceVersion"];

// 서비스 등록 (DI)
builder.Services.AddOpenTelemetry().WithTracing(tcb =>
{
    tcb
    .AddSource(serviceName)
    .SetResourceBuilder(
        ResourceBuilder.CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
    .AddAspNetCoreInstrumentation() // Automatically generate log lines for HTTP requests
    .AddJsonConsoleExporter(); // Output log lines to the console
});
builder.Services.AddMemoryCache();

builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<IDiscordClientManager, DiscordClientManager>();
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<ApiKeyManager>(provider=>
{
    var cache = provider.GetRequiredService<IMemoryCache>();
    var httpClient = provider.GetRequiredService<HttpClient>();
    var url = gameServiceBaseUrl;
    return new ApiKeyManager(cache, configuration, url, httpClient);
});
builder.Services.AddSingleton<APIRequestWrapper>();

builder.Services.AddSingleton<BattleMessenger>(provider=>
{
    var client = provider.GetRequiredService<DiscordSocketClient>();
    var apiWrapper = provider.GetRequiredService<APIRequestWrapper>();
    var url = gameServiceBaseUrl;
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new BattleMessenger(client, apiWrapper, url, configuration);
});
builder.Services.AddSingleton<AdventureMessenger>(provider=>
{
    var client = provider.GetRequiredService<DiscordSocketClient>();
    var apiWrapper = provider.GetRequiredService<APIRequestWrapper>();
    var url = gameServiceBaseUrl;
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new AdventureMessenger(client, apiWrapper, url, configuration);
});

builder.Services.AddSingleton<GameProgressMessenger>(provider=>
{
    var client = provider.GetRequiredService<DiscordSocketClient>();
    var apiWrapper = provider.GetRequiredService<APIRequestWrapper>();
    var url = gameServiceBaseUrl;
    var configuration = provider.GetRequiredService<IConfiguration>();
    return new GameProgressMessenger(client, apiWrapper, url, configuration);
});

// 여기에 컨트롤러 추가도 가능
builder.Services.AddControllers(); // <- REST API 만들고 싶으면 필요

var app = builder.Build();
var serviceProvider = app.Services;

// Web API 라우팅
app.MapControllers();

// 디스코드 봇 실행
var clientManager = app.Services.GetRequiredService<IDiscordClientManager>();
await clientManager.InitClientAsync();
await clientManager.StartClientAsync(token);

var client = app.Services.GetRequiredService<DiscordSocketClient>();
var battle = app.Services.GetRequiredService<BattleMessenger>();
var adventure = app.Services.GetRequiredService<AdventureMessenger>();
var progress = app.Services.GetRequiredService<GameProgressMessenger>();

// Troubleshooting: 버튼 클릭 이벤트 핸들러 등록시에 모든 메신저 클래스에서 핸들러를 등록하면 하나의 버튼에 모든 핸들러의 처리가 실행되는 문제
client.ButtonExecuted += async interaction =>
{
    var id = interaction.Data.CustomId;

    if (id.StartsWith("battle_"))
        await battle.OnButtonExecutedAsync(interaction);
    else if (id.StartsWith("adventure_"))
        await adventure.OnButtonExecutedAsync(interaction);
    else if (id.StartsWith("game_"))
        await progress.OnButtonExecutedAsync(interaction);
    else
        Console.WriteLine($"[❗경고] 처리되지 않은 CustomId: {id}");
};

// 실행
await app.RunAsync();