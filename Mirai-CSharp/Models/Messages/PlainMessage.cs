using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Mirai_CSharp.Models
{
    /// <summary>
    /// 表示文字消息
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public class PlainMessage : Messages
    {
        public const string MsgType = "Plain";

        /// <summary>
        /// 文字消息
        /// </summary>
        [JsonPropertyName("text")]
        public string Message { get; set; } = null!;

        /// <summary>
        /// 初始化 <see cref="PlainMessage"/> 类的新实例
        /// </summary>
        [Obsolete("请使用带参数的构造方法初始化本类实例。")]
        public PlainMessage() : base(MsgType) { }

        /// <summary>
        /// 初始化 <see cref="PlainMessage"/> 类的新实例
        /// </summary>
        /// <param name="message">文字消息内容</param>
        public PlainMessage(string message) : base(MsgType)
        {
            Message = message;
        }

        /// <inheritdoc/>
        public override string ToString() => Message;
    }
}
