using Discord;
using Discord.WebSocket;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Quantization;
using Color = Discord.Color;
using Image = SixLabors.ImageSharp.Image;
using static discpywalNET.Structs;

namespace discpywalNET;

internal static class Program
{
    private static readonly Version Version = new Version(1,0,0,0);
    private const string Prefix = "pywal";
    private static bool _discordNetRawLog;
    private static readonly List<ulong> IdLock = new ();

    private static DiscordSocketClient? _client;
    private static readonly HttpClient HttpClient = new();
  
    private static readonly Activity Activity = new () {
        Text = "for profile changes.",
        Type = ActivityType.Watching
    };

    public static async Task Main(string[] args)
    {
        Console.WriteLine($"discpywalNET {Version.ToString()}");
        _discordNetRawLog = File.Exists(@"shouldLog");

        var config = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.DirectMessages | GatewayIntents.MessageContent | GatewayIntents.GuildEmojis | GatewayIntents.GuildMembers 
        };
        
        _client = new DiscordSocketClient(config);

        _client.Log += Log;
        _client.Ready += ReadyAsync;
        _client.MessageReceived += MessageReceivedAsync;
        _client.GuildMemberUpdated += ClientOnGuildMemberUpdated;
        
        var token = await File.ReadAllTextAsync("token");

        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

        await Task.Delay(-1);
    }

    private static Task<Color> GetDominantColour(Stream stream)
    {
        using var img = Image.Load<Rgba32>(stream);
                
        img.Mutate(x => x
            .Resize(new ResizeOptions
            {
                Sampler = KnownResamplers.NearestNeighbor,
                Size = new Size(100, 0)
            }).Quantize(new OctreeQuantizer(new QuantizerOptions {Dither = null, MaxColors = 1})));

        var dominant = img[0, 0];
        return Task.FromResult(new Color(dominant.R, dominant.G, dominant.B));
    }
    
    private static async Task<Response> UpdateUser(SocketGuildUser? arg2)
    {
        if (arg2 == null) return Response.FatalFailure;
        
        if (IdLock.Contains(arg2.Id))
        {
            if(_discordNetRawLog) Console.WriteLine("IDLocked, role was deleted.");
            IdLock.Remove(arg2.Id);
            return Response.FatalFailure;
        }
        
        IRole? role = arg2.Guild.Roles.FirstOrDefault(x => x.Name == arg2.Id.ToString());
        if (role != null)
        {
            if(_discordNetRawLog) Console.WriteLine($"found role with user ID in {arg2.Guild.Id}");

            var reqUrl = arg2.GetGuildAvatarUrl() ?? arg2.GetAvatarUrl();
            
            using var resp = await HttpClient.GetAsync($"{reqUrl}");
            await using var responseStream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
                
            if (responseStream.Length > 0)
            {
                Color color = await GetDominantColour(responseStream);
                try
                {
                    if (role.Color == color)
                    {
                        if(_discordNetRawLog) Console.WriteLine("colour is the same as the role colour.");
                        return Response.NoChange;
                    }
                    
                    await role.ModifyAsync(roleProperties =>
                    {
                        roleProperties.Color = color;
                    });
                    return Response.Success;
                }
                catch
                {
                    if(_discordNetRawLog) Console.WriteLine("failed to update role, likely no perms or role deleted.");
                    return Response.Failed;
                }
            }

            if(_discordNetRawLog) Console.WriteLine("no response?");
            return Response.Failed;
        }

        if(_discordNetRawLog) Console.WriteLine($"no role with user ID in {arg2.Guild.Id}");
        return Response.NoRole;
    }
    
    private static async Task ClientOnGuildMemberUpdated(Cacheable<SocketGuildUser, ulong> arg1, SocketGuildUser? arg2)
    {
        await UpdateUser(arg2);
    }

    private static async Task MessageReceivedAsync(SocketMessage arg)
    {
        if (_client == null) return;
        
        if (arg.Author.IsBot || arg.Author.Id == _client.CurrentUser.Id) return;
        if (string.IsNullOrEmpty(arg.Content)) return;
        if (!arg.Content.ToLower().StartsWith(Prefix)) return;
        
        string[] args = arg.Content.Substring(Prefix.Length).Trim().Split(' ');
        string command = args.First().ToLower();
        
        var channel = arg.Channel as SocketGuildChannel;
        SocketGuildUser? socketGuildUser = null;
        
        if (channel != null)
        {
            socketGuildUser = channel.Guild.GetUser(arg.Author.Id);
        }

        switch (command)
        {
            case "ping":
                await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: pong. probably.");
                break;
            case "about":
                await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: ```-- discpywalNET {Version.ToString()} --\nwompscode 2025\n\ndominant colour from profile picture changes to role colour!\n" +
                                                   $"successor to discpywal (js) made in C#, because .NET is pretty cool.\n\n"+
                                                   $"repo: https://github.com/wompscode/discpywalNET```");
                break;
            case "create":
                if (channel == null)
                {
                    await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: You are not in a guild channel.");
                    return;
                }
                IRole? createRole = channel.Guild.Roles.FirstOrDefault(x => x.Name == arg.Author.Id.ToString());
                if (createRole == null)
                {
                    try
                    {
                        IRole role = await channel.Guild.CreateRoleAsync(arg.Author.Id.ToString());
                        if (socketGuildUser == null) return;
                        await socketGuildUser.AddRoleAsync(role);
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Role created, colour should get updated - to forcefully trigger an update do `{Prefix}update`.");
                    }
                    catch
                    {
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Failed to create role, likely missing permissions to modify roles.");
                    }
                }
                else
                {
                    await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Role already exists.");
                }
                break;
            case "remove":
                if (channel == null)
                {
                    await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: You are not in a guild channel.");
                    return;
                }
                IRole? removeRole = channel.Guild.Roles.FirstOrDefault(x => x.Name == arg.Author.Id.ToString());
                if (removeRole != null)
                {
                    try
                    {
                        IdLock.Add(arg.Author.Id);
                        await removeRole.DeleteAsync();
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Role deleted.");
                    }
                    catch
                    {
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Failed to delete role, likely missing permissions to modify roles.");
                    }
                }
                else
                {
                    await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Role doesn't exist.");
                }
                break;
            case "update":
                if (channel == null)
                {
                    await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: You are not in a guild channel.");
                    return;
                }
                
                if (socketGuildUser == null) return;
                Response responseUpdate = await UpdateUser(socketGuildUser);
                
                switch (responseUpdate)
                {
                    case Response.Success:
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Forcefully updated role colour.");
                        break;
                    case Response.Failed:
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: Failed to update role colour, likely missing permissions to modify roles.");
                        break;
                    case Response.NoChange:
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: The role colour is up to date.");
                        break;
                    case Response.NoRole:
                        await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: There is no role with your user ID in the server.");
                        break;
                }
                break;
            case "help":
                await arg.Channel.SendMessageAsync($"{arg.Author.Mention}: ```" +
                                                   $"{Prefix}help - this message" +
                                                   $"\n{Prefix}create - create discpywal role" +
                                                   $"\n{Prefix}remove - remove discpywal role" +
                                                   $"\n{Prefix}update - force update" +
                                                   $"\n{Prefix}ping - response test" +
                                                   $"\n{Prefix}about - about discpywalNET```");
                break;
        }
    }

    private static Task ReadyAsync()
    {
        if (_client == null)
        {
            Console.WriteLine("FATAL: somehow reached Ready but no client, exiting");
            Environment.Exit(0);
        }
        
        Console.WriteLine("Connected.");
        _client.SetGameAsync(Activity.Text, null, Activity.Type);
        return Task.CompletedTask;
    }

    private static Task Log(LogMessage msg)
    { 
        if(_discordNetRawLog) Console.WriteLine(msg.ToString());
        return Task.CompletedTask;
    }
}