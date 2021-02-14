using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

#if NET5_0
using System.Net.Http.Json;
#pragma warning disable CS8603 // Possible null reference return.
#else
using System.Net.Http.Headers;
#endif

namespace Mirai_CSharp.Extensions
{
    /// <summary>
    /// <see cref="HttpClient"/> 的扩展方法
    /// </summary>
    public static partial class HttpClientExtensions
    {
        #region PostByteArrayContent
        /// <summary>
        /// 异步发起一个 HttpPost 请求
        /// </summary>
        /// <inheritdoc cref="SendAsync(HttpClient, HttpMethod, Uri, HttpContent?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, Uri uri, byte[] content, CancellationToken token = default)
            => client.PostAsync(uri, new ByteArrayContent(content), token);

        /// <inheritdoc cref="PostAsync(HttpClient, Uri, byte[], CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, byte[] content, CancellationToken token = default)
            => client.PostAsync(new Uri(url), content, token);
        #endregion

        #region PostEmptyContent
        /// <summary>
        /// 异步发起一个 HttpPost 请求
        /// </summary>
        /// <inheritdoc cref="PostAsync(HttpClient, Uri, byte[], CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, Uri uri, CancellationToken token = default)
            => client.PostAsync(uri, null!, token);

        /// <inheritdoc cref="PostAsync(HttpClient, string, byte[], CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, CancellationToken token = default)
            => client.PostAsync(new Uri(url), token);
        #endregion

        #region PostHttpContent
        private static readonly string DefaultBoundary = $"MiraiCSharp/{Assembly.GetExecutingAssembly().GetName().Version}";

        /// <summary>
        /// 异步发起一个 HttpPost 请求
        /// </summary>
        /// <param name="contents">请求正文片段, 将以 multipart/form-data 的形式序列化</param>
        /// <param name="client"></param>
        /// <param name="uri"></param>
        /// <param name="token"></param>
        /// <inheritdoc cref="SendAsync(HttpClient, HttpMethod, Uri, HttpContent?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, Uri uri, IEnumerable<HttpContent> contents, CancellationToken token = default)
        {
            MultipartFormDataContent multipart = new MultipartFormDataContent(DefaultBoundary);
            foreach (HttpContent content in contents)
                multipart.Add(content);

            return client.PostAsync(uri, multipart, token);
        }

        /// <inheritdoc cref="PostAsync(HttpClient, Uri, IEnumerable{HttpContent}, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, IEnumerable<HttpContent> contents, CancellationToken token = default)
            => client.PostAsync(new Uri(url), contents, token);
        #endregion

        #region PostJsonContent
#if !NET5_0
        private static readonly MediaTypeHeaderValue DefaultJsonMediaType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        /// <inheritdoc cref="PostAsJsonAsync{TValue}(HttpClient, Uri, TValue, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsJsonAsync<TValue>(this HttpClient client, Uri uri, TValue value, CancellationToken token = default)
        {
            return client.PostAsJsonAsync(uri, value, null, token);
        }

        /// <inheritdoc cref="PostAsJsonAsync{TValue}(HttpClient, string, TValue, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsJsonAsync<TValue>(this HttpClient client, string url, TValue value, CancellationToken token = default)
        {
            return client.PostAsJsonAsync(url, value, null, token);
        }

        /// <summary>
        /// 异步发起一个 HttpPost 请求
        /// </summary>
        /// <param name="client"></param>
        /// <param name="uri"></param>
        /// <param name="value">作为 Json 正文的对象</param>
        /// <param name="options">序列化 <paramref name="value"/> 时要用到的 <see cref="JsonSerializerOptions"/></param>
        /// <param name="token"></param>
        /// <inheritdoc cref="PostAsync(HttpClient, Uri, byte[], CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsJsonAsync<TValue>(this HttpClient client, Uri uri, TValue value, JsonSerializerOptions? options, CancellationToken token = default)
        {
            ByteArrayContent content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(value, options));
            content.Headers.ContentType = DefaultJsonMediaType;
            return client.PostAsync(uri, content, token);
        }

        /// <param name="client"></param>
        /// <param name="url">请求目标</param>
        /// <param name="value">作为 Json 正文的对象</param>
        /// <param name="options">序列化 <paramref name="value"/> 时要用到的 <see cref="JsonSerializerOptions"/></param>
        /// <param name="token"></param>
        /// <inheritdoc cref="PostAsJsonAsync{TValue}(HttpClient, Uri, TValue, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsJsonAsync<TValue>(this HttpClient client, string url, TValue value, JsonSerializerOptions? options, CancellationToken token = default)
            => client.PostAsJsonAsync(new Uri(url), value, options, token);
#endif
        #endregion

        #region PostStringContent
        /// <summary>
        /// 异步发起一个 HttpPost 请求
        /// </summary>
        /// <param name="encoding">将 <paramref name="content"/> 处理到 <see cref="StringContent"/> 时要用的的一个 <see cref="Encoding"/>。 默认为 <see cref="Encoding.UTF8"/></param>
        /// <param name="client"></param>
        /// <param name="token"></param>
        /// <param name="content"></param>
        /// <param name="uri"></param>
        /// <inheritdoc cref="SendAsync(HttpClient, HttpMethod, Uri, HttpContent?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, Uri uri, string content, Encoding? encoding, CancellationToken token = default)
            => client.PostAsync(uri, new StringContent(content, encoding ?? Encoding.UTF8), token);

        /// <param name="encoding">将 <paramref name="content"/> 处理到 <see cref="StringContent"/> 时要用的的一个 <see cref="Encoding"/>。 默认为 <see cref="Encoding.UTF8"/></param>
        /// <param name="url">请求目标</param>
        /// <param name="client"></param>
        /// <param name="token"></param>
        /// <param name="content"></param>
        /// <inheritdoc cref="PostAsync(HttpClient, Uri, string, Encoding?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, string content, Encoding? encoding, CancellationToken token = default)
            => client.PostAsync(new Uri(url), content, encoding, token);

        /// <inheritdoc cref="PostAsync(HttpClient, Uri, string, Encoding?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, Uri uri, string content, CancellationToken token = default)
            => client.PostAsync(uri, content, null, token);

        /// <inheritdoc cref="PostAsync(HttpClient, string, string, Encoding?, CancellationToken)"/>
        public static Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, string content, CancellationToken token = default)
            => client.PostAsync(url, content, null, token);
        #endregion

        private static Version DefaultHttpVersion { get; } = new Version(2, 0);

        /// <summary>
        /// 异步发起一个 Http 请求
        /// </summary>
        /// <param name="client">要进行请求的 <see cref="HttpClient"/></param>
        /// <param name="method">请求方式</param>
        /// <param name="uri">请求目标</param>
        /// <param name="content">请求正文</param>
        /// <param name="token">用于取消请求的 <see cref="CancellationToken"/></param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="InvalidOperationException"/>
        /// <exception cref="HttpRequestException"/>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<HttpResponseMessage> SendAsync(this HttpClient client, HttpMethod method, Uri uri, HttpContent? content, CancellationToken token = default)
        {
            using HttpRequestMessage request = new HttpRequestMessage(method, uri)
            {
                Content = content,
                Version = DefaultHttpVersion
            };
            return await client.SendAsync(request, token);
        }

#if NET5_0
        /// <summary>
        /// 将服务器响应正文异步序列化为 <see cref="byte"/>[]
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="token">用于取消此操作的一个 <see cref="CancellationToken"/></param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<byte[]> GetBytesAsync(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync(token);
        }

        /// <summary>
        /// 将服务器响应正文异步序列化为 <see cref="string"/>
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="token">用于取消此操作的一个 <see cref="CancellationToken"/></param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<string> GetStringAsync(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync(token);
        }
#else
#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0060 // Remove unused parameter
        /// <summary>
        /// 将服务器响应正文异步序列化为 <see cref="byte"/>[]
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="token">本参数将被忽略, 因为 <see cref="HttpContent.ReadAsByteArrayAsync()"/> 方法没有一个用于接收 <see cref="CancellationToken"/> 的重载</param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<byte[]> GetBytesAsync(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// 将服务器响应正文异步序列化为 <see cref="string"/>
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="token">本参数将被忽略, 因为 <see cref="HttpContent.ReadAsByteArrayAsync()"/> 方法没有一个用于接收 <see cref="CancellationToken"/> 的重载</param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<string> GetStringAsync(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            return await response.Content.ReadAsStringAsync();
        }
#pragma warning restore IDE0060
#pragma warning restore IDE0079
#endif

#if NET5_0
        private static Encoding? GetEncoding(string? charset)
        {
            Encoding? encoding = null;

            if (charset != null)
            {
                try
                {
                    // Remove at most a single set of quotes.
                    encoding = charset.Length > 2 && charset[0] == '\"' && charset[^1] == '\"'
                        ? Encoding.GetEncoding(charset[1..^1])
                        : Encoding.GetEncoding(charset);
                }
                catch (ArgumentException e)
                {
                    throw new InvalidOperationException("The character set provided in ContentType is invalid.", e);
                }
            }

            return encoding;
        }

        /// <inheritdoc cref="GetObjectAsync{T}(Task{HttpResponseMessage}, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<T?> GetObjectAsync<T>(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
            => responseTask.GetObjectAsync<T?>(null, token);

        /// <summary>
        /// 将服务器响应正文异步序列化为 <typeparamref name="T"/> 的一个实例
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="options">反序列化时要使用的 <see cref="JsonSerializerOptions"/></param>
        /// <param name="token">用于取消反序列化的 <see cref="CancellationToken"/></param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<T?> GetObjectAsync<T>(this Task<HttpResponseMessage> responseTask, JsonSerializerOptions? options, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync<T?>(options, token);
        }

        /// <inheritdoc cref="GetObjectAsync(Task{HttpResponseMessage}, Type, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<object?> GetObjectAsync(this Task<HttpResponseMessage> responseTask, Type returnType, CancellationToken token = default)
            => responseTask.GetObjectAsync(returnType, null, token);

        /// <summary>
        /// 将服务器响应正文异步序列化为 <paramref name="returnType"/> 表示的一个实例
        /// </summary>
        /// <param name="returnType">用于转换和返回的 <see cref="Type"/></param>
        /// <param name="responseTask"></param>
        /// <param name="token"></param>
        /// <param name="options"></param>
        /// <inheritdoc cref="GetObjectAsync{T}(Task{HttpResponseMessage}, JsonSerializerOptions?, CancellationToken)"/>
        public static async Task<object?> GetObjectAsync(this Task<HttpResponseMessage> responseTask, Type returnType, JsonSerializerOptions? options, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            return await response.Content.ReadFromJsonAsync(returnType, options, token);
        }

        /// <inheritdoc cref="GetJsonAsync(Task{HttpResponseMessage}, JsonDocumentOptions, CancellationToken)"/>
        public static Task<JsonDocument> GetJsonAsync(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
        {
            return responseTask.GetJsonAsync(default, token);
        }

        /// <summary>
        /// 将服务器响应正文异步反序列化为一个 <see cref="JsonDocument"/> 实例
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="options">反序列化时要使用的 <see cref="JsonDocumentOptions"/></param>
        /// <param name="token">用于取消反序列化的 <see cref="CancellationToken"/></param>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<JsonDocument> GetJsonAsync(this Task<HttpResponseMessage> responseTask, JsonDocumentOptions options, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            Stream stream = response.Content.ReadAsStream(token); // Content.ReadAsStreamAsync 是同步操作
            Encoding? encoding = GetEncoding(response.Content.Headers.ContentType?.CharSet);
            if (encoding != null && encoding != Encoding.UTF8)
                stream = Encoding.CreateTranscodingStream(stream, encoding, Encoding.UTF8);

            using (stream)
            {
                return await JsonDocument.ParseAsync(stream, options, token);
            }
        }
#else
        /// <inheritdoc cref="GetObjectAsync{T}(Task{HttpResponseMessage}, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<T> GetObjectAsync<T>(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
            => responseTask.GetObjectAsync<T>(null, token);

        /// <summary>
        /// 将服务器响应正文异步序列化为 <typeparamref name="T"/> 的一个实例
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="options">反序列化时要使用的 <see cref="JsonSerializerOptions"/></param>
        /// <param name="token">用于取消反序列化的 <see cref="CancellationToken"/></param>
        /// <remarks>
        /// 请确保服务器响应的 Json 是以 UTF-8 编码的
        /// </remarks>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<T> GetObjectAsync<T>(this Task<HttpResponseMessage> responseTask, JsonSerializerOptions? options, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            using Stream stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<T>(stream, options, token);
        }

        /// <inheritdoc cref="GetObjectAsync(Task{HttpResponseMessage}, Type, JsonSerializerOptions?, CancellationToken)"/>
        public static Task<object?> GetObjectAsync(this Task<HttpResponseMessage> responseTask, Type returnType, CancellationToken token = default)
            => responseTask.GetObjectAsync(returnType, null, token);

        /// <summary>
        /// 将服务器响应正文异步序列化为 <paramref name="returnType"/> 表示的一个实例
        /// </summary>
        /// <param name="returnType">用于转换和返回的 <see cref="Type"/></param>
        /// <param name="token"></param>
        /// <param name="options"></param>
        /// <param name="responseTask"></param>
        /// <inheritdoc cref="GetObjectAsync{T}(Task{HttpResponseMessage}, JsonSerializerOptions?, CancellationToken)"/>
        public static async Task<object?> GetObjectAsync(this Task<HttpResponseMessage> responseTask, Type returnType, JsonSerializerOptions? options, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync(stream, returnType, options, token);
        }

        /// <inheritdoc cref="GetJsonAsync(Task{HttpResponseMessage}, JsonDocumentOptions, CancellationToken)"/>
        public static Task<JsonDocument> GetJsonAsync(this Task<HttpResponseMessage> responseTask, CancellationToken token = default)
        {
            return responseTask.GetJsonAsync(default, token);
        }

        /// <summary>
        /// 将服务器响应正文异步反序列化为一个 <see cref="JsonDocument"/> 实例
        /// </summary>
        /// <param name="responseTask">要处理的一个异步请求任务</param>
        /// <param name="options">反序列化时要使用的 <see cref="JsonDocumentOptions"/></param>
        /// <param name="token">用于取消反序列化的 <see cref="CancellationToken"/></param>
        /// <remarks>
        /// 请确保服务器响应的 Json 是以 UTF-8 编码的
        /// </remarks>
        /// <returns>表示此异步操作的 <see cref="Task"/></returns>
        public static async Task<JsonDocument> GetJsonAsync(this Task<HttpResponseMessage> responseTask, JsonDocumentOptions options, CancellationToken token = default)
        {
            using HttpResponseMessage response = await responseTask.ConfigureAwait(false);
            using Stream stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream, options, token);
        }
#endif
    }
}