// 兼容别名门面:完整协议在 super::protocol;历史导入路径(bridge::messages::*)
// 与冒烟/应用层继续可用。
pub use super::protocol::{
    encode_editor_message_value, encode_host_message_value, parse_editor_message,
    parse_editor_message_value, parse_host_message_value, EditorEnvelope as EditorMessage,
    EditorMessageType, HostEnvelope as HostMessage, HostMessageType, ProtocolError,
    MAX_MESSAGE_BYTES, PROTOCOL_VERSION,
};
