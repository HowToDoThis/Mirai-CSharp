using System;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Mirai_CSharp.Models
{
    /// <summary>
    /// 表示xml消息
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public class XmlMessage : Messages
    {
        public const string MsgType = "Xml";

        /// <summary>
        /// Xml原始字符串
        /// </summary>
        [JsonPropertyName("xml")]
        public string Xml { get; set; } = null!;

        /// <summary>
        /// 初始化 <see cref="XmlMessage"/> 类的新实例
        /// </summary>
        [Obsolete("请使用带参数的构造方法初始化本类实例。")]
        public XmlMessage() : base(MsgType) { }

        /// <summary>
        /// 初始化 <see cref="XmlMessage"/> 类的新实例
        /// </summary>
        /// <param name="xml">要发送的原始xml字符串</param>
        public XmlMessage(string xml) : base(MsgType)
        {
            Xml = xml;
        }

        /// <inheritdoc/>
        public override string ToString() => $"[mirai:service:60,{Xml}]"; // Xml的ServiceId=60, https://github.com/mamoe/mirai/blob/2.3-release/mirai-core-api/src/commonMain/kotlin/message/data/RichMessage.kt#L113
    }
}
