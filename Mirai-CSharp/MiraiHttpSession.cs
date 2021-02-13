using Mirai_CSharp.Extensions;
using Mirai_CSharp.Models;
using Mirai_CSharp.Utility;
using Mirai_CSharp.Utility.JsonConverters;
using Mirai_CSharp.Plugin;
using Mirai_CSharp.Exceptions;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Net.Http;
using System.Net.Http.Headers;
#if NET5_0
using System.Net.Http.Json;
#endif
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

namespace Mirai_CSharp
{
    public partial class MiraiHttpSession : IAsyncDisposable
    {
        #region Properties
        private static readonly HttpClient _Client = new HttpClient();

        /// <summary>
        /// Session连接状态
        /// </summary>
        public bool Connected => SessionInfo?.Connected ?? false;

        /// <summary>
        /// Session绑定的QQ号。未连接为 <see langword="null"/>。
        /// </summary>
        public long? QQNumber => SessionInfo?.QQNumber;

        private InternalSessionInfo? SessionInfo;

        private ImmutableList<IPlugin> Plugins = Array.Empty<IPlugin>().ToImmutableList();

        private volatile bool _disposed;
        #endregion

        private class InternalSessionInfo
        {
            public MiraiHttpSessionOptions Options = null!;

            public HttpClient Client = null!;

            public Version ApiVersion = null!;

            public string SessionKey = null!;

            public long QQNumber;

            public CancellationToken Token;

            public CancellationTokenSource Canceller = null!;

            public bool Connected;
        }

        /// <summary>
        /// 初始化 <see cref="MiraiHttpSession"/> 类的新实例
        /// </summary>
        public MiraiHttpSession() { }

        /// <summary>
        /// 添加一个用于处理消息的 <see cref="IPlugin"/>
        /// </summary>
        public void AddPlugin(IPlugin plugin)
        {
            CheckDisposed();
            Plugins = Plugins.Add(plugin);
        }

        /// <summary>
        /// 移除一个用于处理消息的 <see cref="IPlugin"/>。 <paramref name="plugin"/> 必须在之前通过 <see cref="AddPlugin(IPlugin)"/> 添加过
        /// </summary>
        public void RemovePlugin(IPlugin plugin)
        {
            CheckDisposed();
            Plugins = Plugins.Remove(plugin);
        }

        /// <summary>
        /// 异步释放当前Session, 并清理相关资源。
        /// </summary>
        /// <remarks>
        /// 本方法线程安全。
        /// </remarks>
        /// <returns></returns>
        public ValueTask DisposeAsync()
        {
            lock (this)
            {
                if (_disposed)
                    return default;

                _disposed = true;
            }

            InternalSessionInfo? session = Interlocked.Exchange(ref SessionInfo, null);
            if (session != null)
            {
                Plugins = null!;
                foreach (FieldInfo eventField in typeof(MiraiHttpSession).GetEvents().Select(p => typeof(MiraiHttpSession).GetField(p.Name, BindingFlags.NonPublic | BindingFlags.Instance)!))
                    eventField.SetValue(this, null); // 用反射解决掉所有事件的Handler

                return new ValueTask(InternalReleaseAsync(session));
            }

            return default;
        }

        #region API Function

        /// <summary>
        /// 异步获取好友列表
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        public Task<IFriendInfo[]> GetFriendListAsync()
        {
            InternalSessionInfo session = SafeGetSession();
            return session.Client.GetAsync($"{session.Options.BaseUrl}/friendList?sessionKey={session.SessionKey}", session.Token)
                .AsNoSuccCodeApiRespAsync<IFriendInfo[], FriendInfo[]>(session.Token);
        }

        /// <summary>
        /// 异步获取群列表
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        public Task<IGroupInfo[]> GetGroupListAsync()
        {
            InternalSessionInfo session = SafeGetSession();
            return session.Client.GetAsync($"{session.Options.BaseUrl}/groupList?sessionKey={session.SessionKey}", session.Token)
                .AsNoSuccCodeApiRespAsync<IGroupInfo[], GroupInfo[]>(session.Token);
        }

        /// <summary>
        /// 异步获取群成员列表
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="groupNumber">将要进行查询的群号</param>
        public Task<IGroupMemberInfo[]> GetGroupMemberListAsync(long groupNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            return session.Client.GetAsync($"{session.Options.BaseUrl}/memberList?sessionKey={session.SessionKey}&target={groupNumber}", session.Token)
                .AsNoSuccCodeApiRespAsync<IGroupMemberInfo[], GroupMemberInfo[]>(session.Token);
        }

        /// <summary>
        /// 内部使用
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="PermissionDeniedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="action">禁言为 <see langword="true"/>, 解禁为 <see langword="false"/></param>
        /// <param name="groupNumber">将要操作的群号</param>
        private Task InternalToggleMuteAllAsync(bool action, long groupNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/{(action ? "muteAll" : "unmuteAll")}", payload, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步开启全体禁言
        /// </summary>
        /// <param name="groupNumber">将要进行全体禁言的群号</param>
        /// <inheritdoc cref="InternalToggleMuteAllAsync"/>
        public Task MuteAllAsync(long groupNumber)
        {
            return InternalToggleMuteAllAsync(true, groupNumber);
        }

        /// <summary>
        /// 异步关闭全体禁言
        /// </summary>
        /// <param name="groupNumber">将要关闭全体禁言的群号</param>
        /// <inheritdoc cref="InternalToggleMuteAllAsync"/>
        public Task UnmuteAllAsync(long groupNumber)
        {
            return InternalToggleMuteAllAsync(false, groupNumber);
        }

        /// <summary>
        /// 异步禁言给定用户
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="PermissionDeniedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="memberId">将要被禁言的QQ号</param>
        /// <param name="groupNumber">该用户所在群号</param>
        /// <param name="duration">禁言时长。必须介于[1秒, 30天]</param>
        public Task MuteAsync(long memberId, long groupNumber, TimeSpan duration)
        {
            InternalSessionInfo session = SafeGetSession();
            if (duration <= TimeSpan.Zero || duration >= TimeSpan.FromDays(30))
                throw new ArgumentOutOfRangeException(nameof(duration));

            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber,
                memberId,
                time = (int)duration.TotalSeconds
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/mute", payload, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步解禁给定用户
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="PermissionDeniedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="memberId">将要解除禁言的QQ号</param>
        /// <param name="groupNumber">该用户所在群号</param>
        public Task UnmuteAsync(long memberId, long groupNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber,
                memberId,
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/unmute", payload, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步将给定用户踢出给定的群
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="PermissionDeniedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="memberId">将要被踢出的QQ号</param>
        /// <param name="groupNumber">该用户所在群号</param>
        /// <param name="msg">附加消息</param>
        public Task KickMemberAsync(long memberId, long groupNumber, string msg = "您已被移出群聊")
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber,
                memberId,
                msg
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/kick", payload, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步使当前机器人退出给定的群
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="groupNumber">将要退出的群号</param>
        public Task LeaveGroupAsync(long groupNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber,
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/quit", payload, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步修改群信息
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="PermissionDeniedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="groupNumber">要进行修改的群号</param>
        /// <param name="config">群信息。其中不进行修改的值请置为 <see langword="null"/></param>
        public Task ChangeGroupConfigAsync(long groupNumber, IGroupConfig config)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber,
                config
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/groupConfig", payload, JsonSerializeOptionsFactory.IgnoreNulls, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步获取群信息
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="groupNumber">要获取信息的群号</param>
        public Task<IGroupConfig> GetGroupConfigAsync(long groupNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            return session.Client.GetAsync($"{session.Options.BaseUrl}/groupConfig?sessionKey={session.SessionKey}&target={groupNumber}", session.Token)
                .AsApiRespAsync<IGroupConfig, GroupConfig>(session.Token);
        }

        /// <summary>
        /// 异步修改给定群员的信息
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="PermissionDeniedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="memberId">将要修改信息的QQ号</param>
        /// <param name="groupNumber">该用户所在群号</param>
        /// <param name="info">用户信息。其中不进行修改的值请置为 <see langword="null"/></param>
        public Task ChangeGroupMemberInfoAsync(long memberId, long groupNumber, IGroupMemberCardInfo info)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = groupNumber,
                memberId,
                info
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/memberInfo", payload, session.Token)
                .AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步获取给定群员的信息
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="memberId">要获取信息的QQ号</param>
        /// <param name="groupNumber">该用户所在群号</param>
        public Task<IGroupMemberCardInfo> GetGroupMemberInfoAsync(long memberId, long groupNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            return session.Client.GetAsync($"{session.Options.BaseUrl}/memberInfo?sessionKey={session.SessionKey}&target={groupNumber}&memberId={memberId}", session.Token)
                .AsApiRespAsync<IGroupMemberCardInfo, GroupMemberCardInfo>(session.Token);
        }

        #endregion

        #region Application API

        /// <summary>
        /// 异步处理添加好友请求
        /// </summary>
        /// <param name="args">收到添加好友申请事件中的参数, 即<see cref="INewFriendApplyEventArgs"/></param>
        /// <param name="action">处理方式</param>
        /// <param name="message">回复信息</param>
        /// <inheritdoc cref="CommonHandleApplyAsync"/>
        public Task HandleNewFriendApplyAsync(IApplyResponseArgs args, FriendApplyAction action, string message = "")
        {
            return CommonHandleApplyAsync("newFriendRequestEvent", args, (int)action, message);
        }

        /// <summary>
        /// 异步处理加群请求
        /// </summary>
        /// <param name="args">收到用户入群申请事件中的参数, 即 <see cref="IGroupApplyEventArgs"/></param>
        /// <param name="action">处理方式</param>
        /// <param name="message">回复信息</param>
        /// <inheritdoc cref="CommonHandleApplyAsync"/>
        public Task HandleGroupApplyAsync(IApplyResponseArgs args, GroupApplyActions action, string message = "")
        {
            return CommonHandleApplyAsync("memberJoinRequestEvent", args, (int)action, message);
        }

        /// <summary>
        /// 异步处理Bot受邀加群请求
        /// </summary>
        /// <param name="args">Bot受邀入群事件中的参数, 即 <see cref="IBotInvitedJoinGroupEventArgs"/></param>
        /// <param name="action">处理方式</param>
        /// <param name="message">回复信息</param>
        /// <inheritdoc cref="CommonHandleApplyAsync"/>
        public Task HandleBotInvitedJoinGroupAsync(IApplyResponseArgs args, GroupApplyActions action, string message = "")
        {
            return CommonHandleApplyAsync("botInvitedJoinGroupRequestEvent", args, (int)action, message);
        }

        /// <summary>
        /// 内部使用
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <param name="actpath"></param>
        /// <param name="args">Bot受邀入群事件中的参数, 即 <see cref="IBotInvitedJoinGroupEventArgs"/></param>
        /// <param name="action">处理方式</param>
        /// <param name="message">附加信息</param>
        private Task CommonHandleApplyAsync(string actpath, IApplyResponseArgs args, int action, string message)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                eventId = args.EventId,
                fromId = args.FromQQ,
                groupId = args.FromGroup,
                operate = action,
                message
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/resp/{actpath}", payload, session.Token).AsApiRespAsync();
        }

        #endregion

        #region Authentication API

        /// <remarks>
        /// 不会连接到指令监控的ws服务。此方法线程安全。但是在连接过程中, 如果尝试多次调用, 除了第一次以后的所有调用都将立即返回。
        /// </remarks>
        /// <inheritdoc cref="ConnectAsync(MiraiHttpSessionOptions, long, bool)"/>
        public Task ConnectAsync(MiraiHttpSessionOptions options, long qqNumber)
        {
            return ConnectAsync(options, qqNumber, false);
        }

        /// <summary>
        /// 异步连接到mirai-api-http。
        /// </summary>
        /// <remarks>
        /// 此方法线程安全。但是在连接过程中, 如果尝试多次调用, 除了第一次以后的所有调用都将立即返回。
        /// </remarks>
        /// <exception cref="BotNotFoundException"/>
        /// <exception cref="InvalidAuthKeyException"/>
        /// <param name="options">连接信息</param>
        /// <param name="qqNumber">Session将要绑定的Bot的qq号</param>
        /// <param name="listenCommand">是否监听指令相关的消息</param>
        public async Task ConnectAsync(MiraiHttpSessionOptions options, long qqNumber, bool listenCommand)
        {
            CheckDisposed();
            InternalSessionInfo session = new InternalSessionInfo();
            if (Interlocked.CompareExchange(ref SessionInfo, session, null!) == null)
            {
                try
                {
                    session.Client = new HttpClient();
                    session.SessionKey = await AuthorizeAsync(session.Client, options);
                    session.Options = options;
                    await VerifyAsync(session.Client, options, session.SessionKey, qqNumber);

                    session.QQNumber = qqNumber;
                    session.ApiVersion = await GetVersionAsync(session.Client, options);
                    CancellationTokenSource canceller = new CancellationTokenSource();
                    session.Canceller = canceller;
                    session.Token = canceller.Token;
                    session.Connected = true;
                    IMiraiSessionConfig config = await GetConfigAsync(session);
                    if (!config.EnableWebSocket.GetValueOrDefault())
                        await SetConfigAsync(session, new MiraiSessionConfig { CacheSize = config.CacheSize, EnableWebSocket = true });

                    CancellationToken token = session.Canceller.Token;
                    ReceiveMessageLoop(session, token);

                    if (listenCommand)
                        ReceiveCommandLoop(session, token);
                }
                catch
                {
                    Interlocked.CompareExchange(ref SessionInfo, null, session);
                    _ = InternalReleaseAsync(session);
                    throw;
                }
            }
        }

        private static async Task<string> AuthorizeAsync(HttpClient client, MiraiHttpSessionOptions options)
        {
            using JsonDocument j = await client.PostAsJsonAsync($"{options.BaseUrl}/auth", new { authKey = options.AuthKey }).GetJsonAsync();
            JsonElement root = j.RootElement;
            int code = root.GetProperty("code").GetInt32();
            return code switch
            {
                0 => root.GetProperty("session").GetString()!,
                _ => throw GetCommonException(code, in root)
            };
        }

        private static Task VerifyAsync(HttpClient client, MiraiHttpSessionOptions options, string sessionKey, long qqNumber)
        {
            var payload = new
            {
                sessionKey,
                qq = qqNumber
            };

            return client.PostAsJsonAsync($"{options.BaseUrl}/verify", payload).AsApiRespAsync();
        }

        /// <summary>
        /// 异步获取mirai-api-http的版本号
        /// </summary>
        /// <param name="client">要进行请求的 <see cref="HttpClient"/></param>
        /// <param name="options">连接信息</param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<Version> GetVersionAsync(HttpClient client, MiraiHttpSessionOptions options)
        {
            using JsonDocument j = await client.GetAsync($"{options.BaseUrl}/about").GetJsonAsync();
            JsonElement root = j.RootElement;
            int code = root.GetProperty("code").GetInt32();
            if (code == 0)
            {
                string version = root.GetProperty("data").GetProperty("version").GetString()!;
                int vIndex = version.IndexOf('v');
#if NETSTANDARD2_0
                return Version.Parse(vIndex > 0 ? version.Substring(vIndex) : version); // v1.0.0 ~ v1.7.2, skip 'v'
#else
                return Version.Parse(vIndex > 0 ? version[vIndex..] : version); // v1.0.0 ~ v1.7.2, skip 'v'
#endif
            }

            throw GetCommonException(code, in root);
        }

        /// <inheritdoc cref="GetVersionAsync(HttpClient, MiraiHttpSessionOptions)"/>
        public static Task<Version> GetVersionAsync(MiraiHttpSessionOptions options)
        {
            return GetVersionAsync(_Client, options);
        }

        /// <inheritdoc cref="GetVersionAsync(HttpClient, MiraiHttpSessionOptions)"/>
        public Task<Version> GetVersionAsync()
        {
            InternalSessionInfo session = SafeGetSession();
            return GetVersionAsync(session.Client, session.Options);
        }

        /// <summary>
        /// 异步释放Session
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public Task ReleaseAsync(CancellationToken token = default)
        {
            CheckDisposed();
            InternalSessionInfo? session = Interlocked.Exchange(ref SessionInfo, null!);
            return session == null ? throw new InvalidOperationException("请先连接到一个Session。") : InternalReleaseAsync(session, token);
        }

        private static async Task InternalReleaseAsync(InternalSessionInfo session, CancellationToken token = default)
        {
            session.Connected = false;
            session.Canceller?.Cancel();
            session.Canceller?.Dispose();

            var payload = new
            {
                sessionKey = session.SessionKey,
                qq = session.QQNumber
            };

            try
            {
                await session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/release", payload, token).AsApiRespAsync(token);
            }
            finally
            {
                session.Client?.Dispose();
            }
        }

        #endregion

        #region Configuration API

        private static Task<IMiraiSessionConfig> GetConfigAsync(InternalSessionInfo session)
        {
            return session.Client.GetAsync($"{session.Options.BaseUrl}/config?sessionKey={WebUtility.UrlEncode(session.SessionKey)}", session.Token)
                .AsNoSuccCodeApiRespAsync<IMiraiSessionConfig, MiraiSessionConfig>(session.Token);
        }

        private static Task SetConfigAsync(InternalSessionInfo session, IMiraiSessionConfig config)
        {
            var payload = new
            {
                sessionKey = session.SessionKey,
                cacheSize = config.CacheSize,
                enableWebsocket = config.EnableWebSocket
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/config", payload, JsonSerializeOptionsFactory.IgnoreNulls, session.Token).AsApiRespAsync(session.Token);
        }

        /// <summary>
        /// 异步获取当前Session的Config
        /// </summary>
        public Task<IMiraiSessionConfig> GetConfigAsync()
        {
            InternalSessionInfo session = SafeGetSession();
            return GetConfigAsync(session);
        }

        /// <summary>
        /// 异步设置当前Session的Config
        /// </summary>
        /// <param name="config">配置信息</param>
        public Task SetConfigAsync(IMiraiSessionConfig config)
        {
            InternalSessionInfo session = SafeGetSession();
            return SetConfigAsync(session, config);
        }

        #endregion

        #region Command API

        /// <summary>
        /// 异步注册指令
        /// </summary>
        /// <exception cref="InvalidAuthKeyException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <param name="client">要进行请求的 <see cref="HttpClient"/></param>
        /// <param name="options">连接信息</param>
        /// <param name="name">指令名</param>
        /// <param name="alias">指令别名</param>
        /// <param name="description">指令描述</param>
        /// <param name="usage">指令用法, 会在指令执行错误时显示</param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task RegisterCommandAsync(HttpClient client, MiraiHttpSessionOptions options, string name, string[]? alias = null, string? description = null, string? usage = null)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("指令名必须非空。", nameof(name));

            var payload = new
            {
                authKey = options.AuthKey,
                name,
                alias = alias ?? Array.Empty<string>(),
                description,
                usage
            };

            string json = await client.PostAsJsonAsync($"{options.BaseUrl}/command/register", payload).GetStringAsync();
            try
            {
                using JsonDocument j = JsonDocument.Parse(json);
                JsonElement root = j.RootElement;
                int code = root.GetProperty("code").GetInt32();
                if (code != 0)
                    throw GetCommonException(code, in root);
            }
            catch (JsonException) // 返回值非json就是执行失败, 把响应正文重新抛出
            {
                throw new InvalidOperationException(json);
            }
        }

        /// <inheritdoc cref="RegisterCommandAsync(HttpClient, MiraiHttpSessionOptions, string, string[], string, string)"/>
        public static Task RegisterCommandAsync(MiraiHttpSessionOptions options, string name, string[]? alias = null, string? description = null, string? usage = null)
        {
            return RegisterCommandAsync(_Client, options, name, alias, description, usage);
        }

        /// <inheritdoc cref="RegisterCommandAsync(HttpClient, MiraiHttpSessionOptions, string, string[], string, string)"/>
        public Task RegisterCommandAsync(string name, string[]? alias = null, string? description = null, string? usage = null)
        {
            InternalSessionInfo session = SafeGetSession();
            return RegisterCommandAsync(session.Client, session.Options, name, alias, description, usage);
        }

        /// <summary>
        /// 异步执行指令
        /// </summary>
        /// <exception cref="InvalidAuthKeyException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="client">要进行请求的 <see cref="HttpClient"/></param>
        /// <param name="options">连接信息</param>
        /// <param name="name">指令名</param>
        /// <param name="args">指令参数</param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task ExecuteCommandAsync(HttpClient client, MiraiHttpSessionOptions options, string name, params string[] args)
        {
            var payload = new
            {
                authKey = options.AuthKey,
                name,
                args
            };

            string json = await client.PostAsJsonAsync($"{options.BaseUrl}/command/send", payload).GetStringAsync();

            try
            {
                using JsonDocument j = JsonDocument.Parse(json);
                JsonElement root = j.RootElement;
                int code = root.GetProperty("code").GetInt32();
                if (code != 0)
                    throw GetCommonException(code, in root);
            }
            catch (JsonException) // 返回值非json就是执行失败, 把响应正文重新抛出
            {
                throw new InvalidOperationException(json);
            }
            catch (TargetNotFoundException e) // 指令不存在
            {
                e._message = "给定的指令不存在。";
                throw;
            }
        }

        /// <inheritdoc cref="ExecuteCommandAsync(HttpClient, MiraiHttpSessionOptions, string, string[])"/>
        public static Task ExecuteCommandAsync(MiraiHttpSessionOptions options, string name, params string[] args)
        {
            return ExecuteCommandAsync(_Client, options, name, args);
        }

        /// <inheritdoc cref="ExecuteCommandAsync(HttpClient, MiraiHttpSessionOptions, string, string[])"/>
        public Task ExecuteCommandAsync(string name, params string[] args)
        {
            InternalSessionInfo session = SafeGetSession();
            return ExecuteCommandAsync(session.Options, name, args);
        }

        /// <summary>
        /// 异步获取给定QQ的Managers
        /// </summary>
        /// <exception cref="BotNotFoundException"/>
        /// <exception cref="InvalidAuthKeyException"/>
        /// <param name="client">要进行请求的 <see cref="HttpClient"/></param>
        /// <param name="options">连接信息</param>
        /// <param name="qqNumber">机器人QQ号</param>
        /// <param name="token">用于取消操作的Token</param>
        /// <returns>能够管理此机器人的QQ号数组</returns>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static Task<long[]> GetManagersAsync(HttpClient client, MiraiHttpSessionOptions options, long qqNumber, CancellationToken token = default)
        {
            return client.GetAsync($"{options.BaseUrl}/managers?qq={qqNumber}", token).AsNoSuccCodeApiRespAsync<long[]>(token);
        }

        /// <inheritdoc cref="GetManagersAsync(HttpClient, MiraiHttpSessionOptions, long, CancellationToken)"/>
        public static Task<long[]> GetManagersAsync(MiraiHttpSessionOptions options, long qqNumber, CancellationToken token = default)
        {
            return GetManagersAsync(_Client, options, qqNumber, token);
        }

        /// <inheritdoc cref="GetManagersAsync(HttpClient, MiraiHttpSessionOptions, long, CancellationToken)"/>
        public Task<long[]> GetManagersAsync(long qqNumber)
        {
            InternalSessionInfo session = SafeGetSession();
            return GetManagersAsync(session.Client, session.Options, qqNumber, session.Token);
        }

        #endregion

        #region Message API

        private static readonly JsonSerializerOptions _forSendMsg = CreateSendMsgOpt();

        private static JsonSerializerOptions CreateSendMsgOpt()
        {
            JsonSerializerOptions opts = JsonSerializeOptionsFactory.IgnoreNulls;
            opts.Converters.Add(new IMessageBaseArrayConverter());
            return opts;
        }

        /// <summary>
        /// 内部使用
        /// </summary>
        /// <param name="action">api的action</param>
        /// <param name="qqNumber">目标QQ号</param>
        /// <param name="groupNumber">目标所在的群号</param>
        /// <param name="chain">消息链数组。不可为 <see langword="null"/> 或空数组</param>
        /// <param name="quoteMsgId">引用一条消息的messageId进行回复。为 <see langword="null"/> 时不进行引用。</param>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="MessageTooLongException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <returns>用于标识本条消息的 Id</returns>
        private async Task<int> CommonSendMessageAsync(string action, long? qqNumber, long? groupNumber, IMessageBase[] chain, int? quoteMsgId)
        {
            InternalSessionInfo session = SafeGetSession();
            if (chain == null || chain.Length == 0)
                throw new ArgumentException("消息链必须为非空且至少有1条消息。");
            if (chain.OfType<SourceMessage>().Any())
                throw new ArgumentException("无法发送基本信息(SourceMessage)。");
            if (chain.OfType<QuoteMessage>().Any())
                throw new ArgumentException("无法发送引用信息(QuoteMessage), 请使用quoteMsgId参数进行引用。");
            if (chain.All(p => p is PlainMessage pm && string.IsNullOrEmpty(pm.Message)))
                throw new ArgumentException("消息链中的所有消息均为空。");

            var payload = new
            {
                sessionKey = session.SessionKey,
                qq = qqNumber,
                group = groupNumber,
                quote = quoteMsgId,
                messageChain = chain
            };

            using JsonDocument j = await session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/{action}", payload, _forSendMsg).GetJsonAsync(token: session.Token);
            JsonElement root = j.RootElement;
            int code = root.GetProperty("code").GetInt32();
            return code == 0 ? root.GetProperty("messageId").GetInt32() : throw GetCommonException(code, in root);
        }

        /// <summary>
        /// 异步发送好友消息
        /// </summary>
        /// <remarks>
        /// 本方法不会引用回复, 要引用回复, 请调用 <see cref="SendFriendMessageAsync(long, IMessageBase[], int?)"/>
        /// </remarks>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="MessageTooLongException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="qqNumber">目标QQ号</param>
        /// <param name="chain">消息链数组。不可为 <see langword="null"/> 或空数组</param>
        public Task<int> SendFriendMessageAsync(long qqNumber, params IMessageBase[] chain)
        {
            return CommonSendMessageAsync("sendFriendMessage", qqNumber, null, chain, null);
        }

        /// <summary>
        /// 异步发送好友消息
        /// </summary>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="MessageTooLongException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="qqNumber">目标QQ号</param>
        /// <param name="chain">消息链数组。不可为 <see langword="null"/> 或空数组</param>
        /// <param name="quoteMsgId">引用一条消息的messageId进行回复。为 <see langword="null"/> 时不进行引用。</param>
        public Task<int> SendFriendMessageAsync(long qqNumber, IMessageBase[] chain, int? quoteMsgId = null)
        {
            return CommonSendMessageAsync("sendFriendMessage", qqNumber, null, chain, quoteMsgId);
        }

        /// <summary>
        /// 异步发送好友消息
        /// </summary>
        /// <param name="qqNumber">目标QQ号</param>
        /// <param name="builder">构建完毕的 <see cref="IMessageBuilder"/></param>
        /// <param name="quoteMsgId">引用一条消息的messageId进行回复。为 <see langword="null"/> 时不进行引用。</param>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendFriendMessageAsync(long qqNumber, IMessageBuilder builder, int? quoteMsgId = null)
        {
            return CommonSendMessageAsync("sendFriendMessage", qqNumber, null, builder.Build(), quoteMsgId);
        }

        /// <summary>
        /// 异步发送临时消息
        /// </summary>
        /// <remarks>
        /// 本方法不会引用回复, 要引用回复, 请调用 <see cref="SendTempMessageAsync(long, long, IMessageBase[], int?)"/>
        /// </remarks>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendTempMessageAsync(long qqNumber, long groupNumber, params IMessageBase[] chain)
        {
            return CommonSendMessageAsync("sendTempMessage", qqNumber, groupNumber, chain, null);
        }

        /// <summary>
        /// 异步发送临时消息
        /// </summary>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendTempMessageAsync(long qqNumber, long groupNumber, IMessageBase[] chain, int? quoteMsgId = null)
        {
            return CommonSendMessageAsync("sendTempMessage", qqNumber, groupNumber, chain, quoteMsgId);
        }

        /// <summary>
        /// 异步发送临时消息
        /// </summary>
        /// <param name="qqNumber"></param>
        /// <param name="groupNumber"></param>
        /// <param name="builder">构建完毕的 <see cref="IMessageBuilder"/></param>
        /// <param name="quoteMsgId">引用一条消息的messageId进行回复。为 <see langword="null"/> 时不进行引用。</param>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendTempMessageAsync(long qqNumber, long groupNumber, IMessageBuilder builder, int? quoteMsgId = null)
        {
            return CommonSendMessageAsync("sendTempMessage", qqNumber, groupNumber, builder.Build(), quoteMsgId);
        }

        /// <summary>
        /// 异步发送群消息
        /// </summary>
        /// <remarks>
        /// 本方法不会引用回复, 要引用回复, 请调用 <see cref="SendGroupMessageAsync(long, IMessageBase[], int?)"/>
        /// </remarks>
        /// <exception cref="BotMutedException"/>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendGroupMessageAsync(long groupNumber, params IMessageBase[] chain)
        {
            return CommonSendMessageAsync("sendGroupMessage", null, groupNumber, chain, null);
        }

        /// <summary>
        /// 异步发送群消息
        /// </summary>
        /// <exception cref="BotMutedException"/>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendGroupMessageAsync(long groupNumber, IMessageBase[] chain, int? quoteMsgId = null)
        {
            return CommonSendMessageAsync("sendGroupMessage", null, groupNumber, chain, quoteMsgId);
        }

        /// <summary>
        /// 异步发送群消息
        /// </summary>
        /// <exception cref="BotMutedException"/>
        /// <param name="groupNumber">目标QQ群号</param>
        /// <param name="builder">构建完毕的 <see cref="IMessageBuilder"/></param>
        /// <param name="quoteMsgId">引用一条消息的messageId进行回复。为 <see langword="null"/> 时不进行引用。</param>
        /// <inheritdoc cref="CommonSendMessageAsync"/>
        public Task<int> SendGroupMessageAsync(long groupNumber, IMessageBuilder builder, int? quoteMsgId = null)
        {
            return CommonSendMessageAsync("sendGroupMessage", null, groupNumber, builder.Build(), quoteMsgId);
        }

        /// <summary>
        /// 异步撤回消息
        /// </summary>
        /// <param name="messageId">
        /// 请提供以下之一
        /// <list type="bullet">
        /// <item><see cref="SourceMessage.Id"/></item>
        /// <item><see cref="SendFriendMessageAsync(long, IMessageBase[], int?)"/> 的返回值</item>
        /// <item><see cref="SendTempMessageAsync(long, long, IMessageBase[], int?)"/> 的返回值</item>
        /// <item><see cref="SendGroupMessageAsync(long, IMessageBase[], int?)"/> 的返回值</item>
        /// </list>
        /// </param>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="TargetNotFoundException"/>
        public Task RevokeMessageAsync(int messageId)
        {
            InternalSessionInfo session = SafeGetSession();
            var payload = new
            {
                sessionKey = session.SessionKey,
                target = messageId
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/recall", payload, session.Token).AsApiRespAsync(session.Token);
        }

        #endregion

        #region Send Image API

        /// <summary>
        /// 内部使用
        /// </summary>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="NotSupportedException"/>
        /// <exception cref="TargetNotFoundException"/>
        /// <param name="qqNumber">目标QQ号</param>
        /// <param name="groupNumber">目标QQ号所在的群号</param>
        /// <param name="urls">一个Url数组。不可为 <see langword="null"/> 或空数组</param>
        /// <returns>一组ImageId</returns>
        private Task<string[]> CommonSendImageAsync(long? qqNumber, long? groupNumber, string[] urls)
        {
            InternalSessionInfo session = SafeGetSession();
            if (urls == null || urls.Length == 0)
                throw new ArgumentException("urls必须为非空且至少有1条url。");

            var payload = new
            {
                sessionKey = session.SessionKey,
                qq = qqNumber,
                group = groupNumber,
                urls
            };

            return session.Client.PostAsJsonAsync($"{session.Options.BaseUrl}/sendImageMessage", payload, JsonSerializeOptionsFactory.IgnoreNulls, session.Token)
                .AsNoSuccCodeApiRespAsync<string[]>();
        }

        /// <summary>
        /// 异步发送给定Url数组中的图片到给定好友
        /// </summary>
        /// <inheritdoc cref="CommonSendImageAsync"/>
        public Task<string[]> SendImageToFriendAsync(long qqNumber, string[] urls)
        {
            return CommonSendImageAsync(qqNumber, null, urls);
        }

        /// <summary>
        /// 异步发送给定Url数组中的图片到临时会话
        /// </summary>
        /// <inheritdoc cref="CommonSendImageAsync"/>
        public Task<string[]> SendImageToTempAsync(long qqNumber, long groupNumber, string[] urls)
        {
            return CommonSendImageAsync(qqNumber, groupNumber, urls);
        }

        /// <summary>
        /// 异步发送给定Url数组中的图片到群
        /// </summary>
        /// <exception cref="BotMutedException"/>
        /// <inheritdoc cref="CommonSendImageAsync"/>
        public Task<string[]> SendImageToGroupAsync(long groupNumber, string[] urls)
        {
            return CommonSendImageAsync(null, groupNumber, urls);
        }

        /// <summary>
        /// 内部使用
        /// </summary>
        /// <exception cref="InvalidOperationException"/>
        /// <param name="session"></param>
        /// <param name="type">目标类型</param>
        /// <param name="imgStream">图片流</param>
        /// <remarks>
        /// 注意: 当 mirai-api-http 的版本小于等于v1.7.0时, 本方法返回的将是一个只有 Url 有值的 <see cref="ImageMessage"/>
        /// </remarks>
        /// <returns>一个 <see cref="ImageMessage"/> 实例, 可用于以后的消息发送</returns>
        private static Task<ImageMessage> InternalUploadPictureAsync(InternalSessionInfo session, UploadTarget type, Stream imgStream)
        {
            if (session.ApiVersion <= new Version(1, 7, 0))
            {
                Guid guid = Guid.NewGuid();
                ImageHttpListener.RegisterImage(guid, imgStream);
                return Task.FromResult(new ImageMessage(null, $"http://127.0.0.1:{ImageHttpListener.Port}/fetch?guid={guid:n}", null));
            }

            HttpContent sessionKeyContent = new StringContent(session.SessionKey);
            sessionKeyContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "sessionKey"
            };

            HttpContent typeContent = new StringContent(type.ToString().ToLower());
            typeContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "type"
            };

            string format;
            using (Image img = Image.FromStream(imgStream))
            {
                format = img.RawFormat.ToString();
                switch (format)
                {
                    case nameof(ImageFormat.Jpeg):
                    case nameof(ImageFormat.Png):
                    case nameof(ImageFormat.Gif):
                        {
                            format = format.ToLower();
                            break;
                        }
                    default: // 不是以上三种类型的图片就强转为Png
                        {
                            MemoryStream ms = new MemoryStream();
                            img.Save(ms, ImageFormat.Png);
                            imgStream.Dispose();
                            imgStream = ms;
                            format = "png";
                            break;
                        }
                }
            }

            imgStream.Seek(0, SeekOrigin.Begin);

            HttpContent imageContent = new StreamContent(imgStream);
            imageContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "img",
                FileName = $"{Guid.NewGuid():n}.{format}"
            };

            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/" + format);
            HttpContent[] contents = new HttpContent[]
            {
                sessionKeyContent,
                typeContent,
                imageContent
            };

            return session.Client.PostAsync($"{session.Options.BaseUrl}/uploadImage", contents, session.Token)
                .AsNoSuccCodeApiRespAsync<ImageMessage>(session.Token)
                .ContinueWith(t => t.IsFaulted && t.Exception!.InnerException is JsonException ? throw new NotSupportedException("当前版本的mirai-api-http无法发送图片。") : t, TaskContinuationOptions.ExecuteSynchronously).Unwrap();
            //  ^-- 处理 JsonException 到 NotSupportedException, https://github.com/mamoe/mirai-api-http/issues/85
        }

        /// <summary>
        /// 异步上传图片
        /// </summary>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="FileNotFoundException"/>
        /// <param name="type">类型</param>
        /// <param name="imagePath">图片路径</param>
        /// <inheritdoc cref="InternalUploadPictureAsync"/>
        public Task<ImageMessage> UploadPictureAsync(UploadTarget type, string imagePath)
        {
            InternalSessionInfo session = SafeGetSession();
            return InternalUploadPictureAsync(session, type, new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        /// <summary>
        /// 异步上传图片
        /// </summary>
        /// <exception cref="ArgumentException"/>
        /// <param name="type">类型</param>
        /// <param name="image">图片流</param>
        /// <inheritdoc cref="InternalUploadPictureAsync"/>
        public Task<ImageMessage> UploadPictureAsync(UploadTarget type, Stream image)
        {
            InternalSessionInfo session = SafeGetSession();
            return InternalUploadPictureAsync(session, type, image);
        }

        /// <summary>
        /// 异步上传图片
        /// </summary>
        /// <exception cref="ArgumentException"/>
        /// <exception cref="FileNotFoundException"/>
        /// <param name="type">类型</param>
        /// <param name="imagePath">图片路径</param>
        /// <inheritdoc cref="InternalUploadPictureAsync"/>
        [Obsolete("请调用 UploadPictureAsync(UploadTarget, string)")]
        public Task<ImageMessage> UploadPictureAsync(PictureTarget type, string imagePath)
        {
            InternalSessionInfo session = SafeGetSession();
            return InternalUploadPictureAsync(session, (UploadTarget)(int)type, new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        /// <summary>
        /// 异步上传图片
        /// </summary>
        /// <exception cref="ArgumentException"/>
        /// <param name="type">类型</param>
        /// <param name="image">图片流</param>
        /// <inheritdoc cref="InternalUploadPictureAsync"/>
        [Obsolete("请调用 UploadPictureAsync(UploadTarget, Stream)")]
        public Task<ImageMessage> UploadPictureAsync(PictureTarget type, Stream image)
        {
            InternalSessionInfo session = SafeGetSession();
            return InternalUploadPictureAsync(session, (UploadTarget)(int)type, image);
        }

        #endregion

        #region Send Voice API

        /// <summary>
        /// 内部使用
        /// </summary>
        /// <exception cref="NotSupportedException"/>
        /// <param name="session"></param>
        /// <param name="type">目标类型</param>
        /// <param name="voiceStream">语音流</param>
        /// <returns>一个 <see cref="VoiceMessage"/> 实例, 可用于以后的消息发送</returns>
        private static Task<VoiceMessage> InternalUploadVoiceAsync(InternalSessionInfo session, UploadTarget type, Stream voiceStream)
        {
            if (session.ApiVersion < new Version(1, 8, 0))
                throw new NotSupportedException($"当前版本的mirai-api-http不支持上传语音。({session.ApiVersion}, 必须>=1.8.0)");

            HttpContent sessionKeyContent = new StringContent(session.SessionKey);
            sessionKeyContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "sessionKey"
            };

            HttpContent typeContent = new StringContent(type.ToString().ToLower());
            typeContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "type"
            };

            HttpContent voiceContent = new StreamContent(voiceStream);
            voiceContent.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
            {
                Name = "voice",
                FileName = $"{Guid.NewGuid():n}.amr"
            };

            HttpContent[] contents = new HttpContent[]
            {
                sessionKeyContent,
                typeContent,
                voiceContent
            };

            return session.Client.PostAsync($"{session.Options.BaseUrl}/uploadVoice", contents, session.Token)
                .AsNoSuccCodeApiRespAsync<VoiceMessage>(session.Token);
        }

        /// <summary>
        /// 异步上传语音
        /// </summary>
        /// <exception cref="FileNotFoundException"/>
        /// <param name="type">类型</param>
        /// <param name="voicePath">语音路径</param>
        /// <inheritdoc cref="InternalUploadVoiceAsync"/>
        public Task<VoiceMessage> UploadVoiceAsync(UploadTarget type, string voicePath)
        {
            InternalSessionInfo session = SafeGetSession();
            return InternalUploadVoiceAsync(session, type, new FileStream(voicePath, FileMode.Open, FileAccess.Read, FileShare.Read));
        }

        /// <summary>
        /// 异步上传语音
        /// </summary>
        /// <param name="type">类型</param>
        /// <param name="voice">语音流</param>
        /// <inheritdoc cref="InternalUploadVoiceAsync"/>
        public Task<VoiceMessage> UploadVoiceAsync(UploadTarget type, Stream voice)
        {
            InternalSessionInfo session = SafeGetSession();
            return InternalUploadVoiceAsync(session, type, voice);
        }

        #endregion

        #region Validation
        /// <summary>
        /// 通过状态码返回相应的异常
        /// </summary>
        /// <returns>
        /// 根据给定的 <paramref name="code"/> 返回下列异常之一:
        /// <list type="bullet">
        /// <item><term><see cref="InvalidAuthKeyException"/></term><description><paramref name="code"/> 为 1</description></item>
        /// <item><term><see cref="BotNotFoundException"/></term><description><paramref name="code"/> 为 2</description></item>
        /// <item><term><see cref="InvalidSessionException"/></term><description><paramref name="code"/> 为 3 或 4</description></item>
        /// <item><term><see cref="TargetNotFoundException"/></term><description><paramref name="code"/> 为 5</description></item>
        /// <item><term><see cref="FileNotFoundException"/></term><description><paramref name="code"/> 为 6</description></item>
        /// <item><term><see cref="PermissionDeniedException"/></term><description><paramref name="code"/> 为 10</description></item>
        /// <item><term><see cref="BotMutedException"/></term><description><paramref name="code"/> 为 20</description></item>
        /// <item><term><see cref="MessageTooLongException"/></term><description><paramref name="code"/> 为 30</description></item>
        /// <item><term><see cref="ArgumentException"/></term><description><paramref name="code"/> 为 400</description></item>
        /// <item><term><see cref="UnknownResponseException"/></term><description>其它情况</description></item>
        /// </list>
        /// </returns>
        internal static Exception GetCommonException(int code, in JsonElement root)
        {
            return code switch
            {
                1 => new InvalidAuthKeyException(),
                2 => new BotNotFoundException(),
                // 3 or 4 => new InvalidSessionException(), // C# 9.0
                3 => new InvalidSessionException(),
                4 => new InvalidSessionException(),
                5 => new TargetNotFoundException(),
                6 => new FileNotFoundException("指定的文件不存在。"),
                10 => new PermissionDeniedException(),
                20 => new BotMutedException(),
                30 => new MessageTooLongException(),
                400 => new ArgumentException("调用http-api失败, 参数错误, 请到 https://github.com/Executor-Cheng/Mirai-CSharp/issues 下提交issue。"),
                _ => new UnknownResponseException(root.GetRawText())
            };
        }

        private void CheckDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MiraiHttpSession));
        }

        private InternalSessionInfo SafeGetSession()
        {
            CheckDisposed();
            InternalSessionInfo? session = SessionInfo;
            return session ?? throw new InvalidOperationException("请先连接到一个Session。");
        }

        #endregion

        #region Event

        /// <summary>
        /// 通用事件委托
        /// </summary>
        /// <typeparam name="TEventArgs">事件参数</typeparam>
        /// <param name="sender">调用此委托的 <see cref="MiraiHttpSession"/></param>
        /// <param name="e">事件参数</param>
        public delegate Task<bool> CommonEventHandler<TEventArgs>(MiraiHttpSession sender, TEventArgs e);
        /// <summary>
        /// 与mirai-api-http的ws连接被异常断开
        /// </summary>
        public event CommonEventHandler<Exception>? DisconnectedEvt;
        /// <summary>
        /// 收到未知消息。如有需要, 请自行解析
        /// </summary>
        public event CommonEventHandler<IUnknownMessageEventArgs>? UnknownMessageEvt;

        #region Bot Action
        /// <summary>
        /// Bot登录成功
        /// </summary>
        public event CommonEventHandler<IBotOnlineEventArgs>? BotOnlineEvt;
        /// <summary>
        /// Bot主动离线
        /// </summary>
        public event CommonEventHandler<IBotPositiveOfflineEventArgs>? BotPositiveOfflineEvt;
        /// <summary>
        /// Bot被挤下线
        /// </summary>
        public event CommonEventHandler<IBotKickedOfflineEventArgs>? BotKickedOfflineEvt;
        /// <summary>
        /// Bot意外断开连接(服务器主动断开连接、网络问题等等)
        /// </summary>
        public event CommonEventHandler<IBotDroppedEventArgs>? BotDroppedEvt;
        /// <summary>
        /// Bot主动重新登录
        /// </summary>
        public event CommonEventHandler<IBotReloginEventArgs>? BotReloginEvt;
        /// <summary>
        /// Bot在群里的权限被改变. 操作人一定是群主
        /// </summary>
        public event CommonEventHandler<IBotGroupPermissionChangedEventArgs>? BotGroupPermissionChangedEvt;
        /// <summary>
        /// Bot被禁言
        /// </summary>
        public event CommonEventHandler<IBotMutedEventArgs>? BotMutedEvt;
        /// <summary>
        /// Bot被取消禁言
        /// </summary>
        public event CommonEventHandler<IBotUnmutedEventArgs>? BotUnmutedEvt;
        /// <summary>
        /// Bot加入了一个新群
        /// </summary>
        public event CommonEventHandler<IBotJoinedGroupEventArgs>? BotJoinedGroupEvt;
        /// <summary>
        /// Bot主动退出一个群
        /// </summary>
        public event CommonEventHandler<IBotPositiveLeaveGroupEventArgs>? BotPositiveLeaveGroupEvt;
        /// <summary>
        /// Bot被踢出一个群
        /// </summary>
        public event CommonEventHandler<IBotKickedOutEventArgs>? BotKickedOutEvt;
        /// <summary>
        /// Bot被邀请入群
        /// </summary>
        public event CommonEventHandler<IBotInvitedJoinGroupEventArgs>? BotInvitedJoinGroupEvt;
        #endregion

        #region Command
        /// <summary>
        /// 指令执行后引发的事件
        /// </summary>
        public event CommonEventHandler<ICommandExecutedEventArgs>? CommandExecuted;
        #endregion

        #region Friend Action
        /// <summary>
        /// 收到好友消息
        /// </summary>
        public event CommonEventHandler<IFriendMessageEventArgs>? FriendMessageEvt;
        /// <summary>
        /// 好友消息被撤回
        /// </summary>
        public event CommonEventHandler<IFriendMessageRevokedEventArgs>? FriendMessageRevokedEvt;
        /// <summary>
        /// 收到添加好友申请
        /// </summary>
        public event CommonEventHandler<INewFriendApplyEventArgs>? NewFriendApplyEvt;
        #endregion

        #region Group Action
        /// <summary>
        /// 收到群消息
        /// </summary>
        public event CommonEventHandler<IGroupMessageEventArgs>? GroupMessageEvt;
        /// <summary>
        /// 群消息被撤回
        /// </summary>
        public event CommonEventHandler<IGroupMessageRevokedEventArgs>? GroupMessageRevokedEvt;
        /// <summary>
        /// 某个群名被改变
        /// </summary>
        public event CommonEventHandler<IGroupNameChangedEventArgs>? GroupNameChangedEvt;
        /// <summary>
        /// 某群入群公告改变
        /// </summary>
        public event CommonEventHandler<IGroupEntranceAnnouncementChangedEventArgs>? GroupEntranceAnnouncementChangedEvt;
        /// <summary>
        /// 全员禁言设置被改变
        /// </summary>
        public event CommonEventHandler<IGroupMuteAllChangedEventArgs>? GroupMuteAllChangedEvt;
        /// <summary>
        /// 匿名聊天设置被改变
        /// </summary>
        public event CommonEventHandler<IGroupAnonymousChatChangedEventArgs>? GroupAnonymousChatChangedEvt;
        /// <summary>
        /// 坦白说设置被改变
        /// </summary>
        public event CommonEventHandler<IGroupConfessTalkChangedEventArgs>? GroupConfessTalkChangedEvt;
        /// <summary>
        /// 群员邀请好友加群设置被改变
        /// </summary>
        public event CommonEventHandler<IGroupMemberInviteChangedEventArgs>? GroupMemberInviteChangedEvt;
        /// <summary>
        /// 新人入群
        /// </summary>
        public event CommonEventHandler<IGroupMemberJoinedEventArgs>? GroupMemberJoinedEvt;
        /// <summary>
        /// 成员主动离群（该成员不是Bot, 见 <see cref="BotPositiveLeaveGroupEvt"/>）
        /// </summary>
        public event CommonEventHandler<IGroupMemberPositiveLeaveEventArgs>? GroupMemberPositiveLeaveEvt;
        /// <summary>
        /// 成员被踢出群（该成员不是Bot, 见 <see cref="BotKickedOutEvt"/>）
        /// </summary>
        public event CommonEventHandler<IGroupMemberKickedEventArgs>? GroupMemberKickedEvt;
        /// <summary>
        /// 群名片改动
        /// </summary>
        public event CommonEventHandler<IGroupMemberCardChangedEventArgs>? GroupMemberCardChangedEvt;
        /// <summary>
        /// 群头衔改动（只有群主有操作限权）
        /// </summary>
        public event CommonEventHandler<IGroupMemberSpecialTitleChangedEventArgs>? GroupMemberSpecialTitleChangedEvt;
        /// <summary>
        /// 成员权限改变（该成员不是Bot, 见 <see cref="BotGroupPermissionChangedEvt"/>）
        /// </summary>
        public event CommonEventHandler<IGroupMemberPermissionChangedEventArgs>? GroupMemberPermissionChangedEvt;
        /// <summary>
        /// 群成员被禁言（该成员不可能是Bot, 见 <see cref="BotMutedEventArgs"/>）
        /// </summary>
        public event CommonEventHandler<IGroupMemberMutedEventArgs>? GroupMemberMutedEvt;
        /// <summary>
        /// 群成员被取消禁言（该成员不可能是Bot, 见 <see cref="BotUnmutedEventArgs"/>）
        /// </summary>
        public event CommonEventHandler<IGroupMemberUnmutedEventArgs>? GroupMemberUnmutedEvt;
        /// <summary>
        /// 收到用户入群申请（Bot需要有 <see cref="GroupPermission.Administrator"/> 或 <see cref="GroupPermission.Owner"/> 权限）
        /// </summary>
        public event CommonEventHandler<IGroupApplyEventArgs>? GroupApplyEvt;
        #endregion

        #region Temp Message
        /// <summary>
        /// 收到临时消息
        /// </summary>
        public event CommonEventHandler<ITempMessageEventArgs>? TempMessageEvt;
        #endregion

        #endregion

        #region Invoking

        private static async Task InvokeAsync<TEventArgs>(IEnumerable<IPlugin> plugins, CommonEventHandler<TEventArgs>? handlers, MiraiHttpSession session, TEventArgs e)
        {
            try
            {
                foreach (IPlugin plugin in plugins)
                {
                    if (plugin is IPlugin<TEventArgs> tPlugin && await tPlugin.HandleEvent(session, e))
                        return;
                }

                if (handlers != null)
                    await InvokeAsync(handlers, session, e);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                throw;
            }
        }

        private static async Task InvokeAsync<TEventArgs>(CommonEventHandler<TEventArgs> handlers, MiraiHttpSession sender, TEventArgs e)
        {
            foreach (CommonEventHandler<TEventArgs> handler in handlers.GetInvocationList())
            {
                if (await handler.Invoke(sender, e))
                    break;
            }
        }

        #endregion

        #region Receive Message

        private async void ReceiveMessageLoop(InternalSessionInfo session, CancellationToken token)
        {
            using ClientWebSocket ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri($"ws://{session.Options.Host}:{session.Options.Port}/all?sessionKey={session.SessionKey}"), token);
                while (true)
                {
                    using MemoryStream ms = await ws.ReceiveFullyAsync(token);
                    JsonElement root = JsonSerializer.Deserialize<JsonElement>(new ReadOnlySpan<byte>(ms.GetBuffer(), 0, (int)ms.Length));
                    switch (root.GetProperty("type").GetString())
                    {
                        case "BotOnlineEvent":
                            {
                                _ = InvokeAsync(Plugins, BotOnlineEvt, this, root.Deserialize<BotEventArgs>());
                                break;
                            }
                        case "BotOfflineEventActive":
                            {
                                _ = InvokeAsync(Plugins, BotPositiveOfflineEvt, this, root.Deserialize<BotEventArgs>());
                                break;
                            }
                        case "BotOfflineEventForce":
                            {
                                _ = InvokeAsync(Plugins, BotKickedOfflineEvt, this, root.Deserialize<BotEventArgs>());
                                break;
                            }
                        case "BotOfflineEventDropped":
                            {
                                _ = InvokeAsync(Plugins, BotDroppedEvt, this, root.Deserialize<BotEventArgs>());
                                break;
                            }
                        case "BotReloginEvent":
                            {
                                _ = InvokeAsync(Plugins, BotReloginEvt, this, root.Deserialize<BotEventArgs>());
                                break;
                            }
                        case "BotInvitedJoinGroupRequestEvent":
                            {
                                _ = InvokeAsync(Plugins, BotInvitedJoinGroupEvt, this, root.Deserialize<CommonGroupApplyEventArgs>());
                                break;
                            }
                        case "FriendMessage":
                            {
                                _ = InvokeAsync(Plugins, FriendMessageEvt, this, root.Deserialize<FriendMessageEventArgs>());
                                break;
                            }
                        case "GroupMessage":
                            {
                                _ = InvokeAsync(Plugins, GroupMessageEvt, this, root.Deserialize<GroupMessageEventArgs>());
                                break;
                            }
                        case "TempMessage":
                            {
                                _ = InvokeAsync(Plugins, TempMessageEvt, this, root.Deserialize<TempMessageEventArgs>());
                                break;
                            }
                        case "GroupRecallEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMessageRevokedEvt, this, root.Deserialize<GroupMessageRevokedEventArgs>());
                                break;
                            }
                        case "FriendRecallEvent":
                            {
                                _ = InvokeAsync(Plugins, FriendMessageRevokedEvt, this, root.Deserialize<FriendMessageRevokedEventArgs>());
                                break;
                            }
                        case "BotGroupPermissionChangeEvent":
                            {
                                _ = InvokeAsync(Plugins, BotGroupPermissionChangedEvt, this, root.Deserialize<BotGroupPermissionChangedEventArgs>());
                                break;
                            }
                        case "BotMuteEvent":
                            {
                                _ = InvokeAsync(Plugins, BotMutedEvt, this, root.Deserialize<BotMutedEventArgs>());
                                break;
                            }
                        case "BotUnmuteEvent":
                            {
                                _ = InvokeAsync(Plugins, BotUnmutedEvt, this, root.Deserialize<BotUnmutedEventArgs>());
                                break;
                            }
                        case "BotJoinGroupEvent":
                            {
                                _ = InvokeAsync(Plugins, BotJoinedGroupEvt, this, root.Deserialize<GroupEventArgs>());
                                break;
                            }
                        case "BotLeaveEventActive":
                            {
                                _ = InvokeAsync(Plugins, BotPositiveLeaveGroupEvt, this, root.Deserialize<GroupEventArgs>());
                                break;
                            }
                        case "BotLeaveEventKick":
                            {
                                _ = InvokeAsync(Plugins, BotKickedOutEvt, this, root.Deserialize<GroupEventArgs>());
                                break;
                            }
                        case "GroupNameChangeEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupNameChangedEvt, this, root.Deserialize<GroupStringPropertyChangedEventArgs>());
                                break;
                            }
                        case "GroupEntranceAnnouncementChangeEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupEntranceAnnouncementChangedEvt, this, root.Deserialize<GroupStringPropertyChangedEventArgs>());
                                break;
                            }
                        case "GroupMuteAllEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMuteAllChangedEvt, this, root.Deserialize<GroupBoolPropertyChangedEventArgs>());
                                break;
                            }
                        case "GroupAllowAnonymousChatEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupAnonymousChatChangedEvt, this, root.Deserialize<GroupBoolPropertyChangedEventArgs>());
                                break;
                            }
                        case "GroupAllowConfessTalkEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupConfessTalkChangedEvt, this, root.Deserialize<GroupBoolPropertyChangedEventArgs>());
                                break;
                            }
                        case "GroupAllowMemberInviteEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberInviteChangedEvt, this, root.Deserialize<GroupBoolPropertyChangedEventArgs>());
                                break;
                            }
                        case "MemberJoinEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberJoinedEvt, this, root.Deserialize<MemberEventArgs>());
                                break;
                            }
                        case "MemberLeaveEventKick":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberKickedEvt, this, root.Deserialize<MemberOperatingEventArgs>());
                                break;
                            }
                        case "MemberLeaveEventQuit":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberPositiveLeaveEvt, this, root.Deserialize<MemberEventArgs>());
                                break;
                            }
                        case "MemberCardChangeEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberCardChangedEvt, this, root.Deserialize<GroupMemberStringPropertyChangedEventArgs>());
                                break;
                            }
                        case "MemberSpecialTitleChangeEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberSpecialTitleChangedEvt, this, root.Deserialize<GroupMemberStringPropertyChangedEventArgs>());
                                break;
                            }
                        case "MemberPermissionChangeEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberPermissionChangedEvt, this, root.Deserialize<GroupMemberPermissionChangedEventArgs>());
                                break;
                            }
                        case "MemberMuteEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberMutedEvt, this, root.Deserialize<GroupMemberMutedEventArgs>());
                                break;
                            }
                        case "MemberUnmuteEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupMemberUnmutedEvt, this, root.Deserialize<GroupMemberUnmutedEventArgs>());
                                break;
                            }
                        case "NewFriendRequestEvent":
                            {
                                _ = InvokeAsync(Plugins, NewFriendApplyEvt, this, root.Deserialize<NewFriendApplyEventArgs>());
                                break;
                            }
                        case "MemberJoinRequestEvent":
                            {
                                _ = InvokeAsync(Plugins, GroupApplyEvt, this, root.Deserialize<CommonGroupApplyEventArgs>());
                                break;
                            }
                        default:
                            {
                                _ = InvokeAsync(Plugins, UnknownMessageEvt, this, new UnknownMessageEventArgs(root.Clone()));
                                break;
                            }
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                if (Interlocked.CompareExchange(ref SessionInfo, null, session) != null)
                {
                    _ = InternalReleaseAsync(session, CancellationToken.None); // 不异步等待, 省的抛错没地捕获
                    try { DisconnectedEvt?.Invoke(this, e); } catch { } // 扔掉所有异常
                }
            }
        }

        private async void ReceiveCommandLoop(InternalSessionInfo session, CancellationToken token)
        {
            using ClientWebSocket ws = new ClientWebSocket();
            try
            {
                await ws.ConnectAsync(new Uri($"ws://{session.Options.Host}:{session.Options.Port}/command?authKey={session.Options.AuthKey}"), token);
                while (true)
                {
                    using MemoryStream ms = await ws.ReceiveFullyAsync(token);
                    ms.Seek(0, SeekOrigin.Begin);
                    using JsonDocument j = await JsonDocument.ParseAsync(ms, default, token);
                    JsonElement root = j.RootElement;
                    _ = InvokeAsync(Plugins, CommandExecuted, this, root.Deserialize<CommandExecutedEventArgs>());
                }
            }
            catch (OperationCanceledException)
            {

            }
            catch (Exception e)
            {
                if (Interlocked.CompareExchange(ref SessionInfo, null, session) != null)
                {
                    _ = InternalReleaseAsync(session, CancellationToken.None); // 不异步等待, 省的抛错没地捕获
                    try { DisconnectedEvt?.Invoke(this, e); } catch { } // 扔掉所有异常
                }
            }
        }

        #endregion
    }
}
