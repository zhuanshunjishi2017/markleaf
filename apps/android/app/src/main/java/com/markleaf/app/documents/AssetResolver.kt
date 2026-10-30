package com.markleaf.app.documents

/**
 * 文档图片解析（spec §3.1 assets.local 拦截器的数据面）。
 *
 * 设计要点：
 * - 纯路径逻辑可 JVM 测试（loader 注入：DocumentRef 目录列表/读取由 SAF 完成）
 * - 归一化后必须落在授权根内；URL 编码在解析前先解码再复检（防 %2e%2e 逃逸）
 * - 返回 null = 不提供（WebViewClient 收到 null 会按正常网络请求处理，
 *   assets.local 无真实服务器 → 404，不会落到外网）
 */
class AssetResolver(private val loader: (path: String) -> ByteArray?) {

    /**
     * @param path 前端请求的 path 参数（文档相对路径或绝对路径）
     * @param documentDir 文档所在目录：POSIX 目录路径，或 SAF 目录的
     *   `content://` document URI 字符串（此时不能当 POSIX 前缀拼接——
     *   URI 会被拆成垃圾段；只归一化相对路径，目录定位由 SAF 侧按段行走）
     */
    fun resolve(path: String, documentDir: String): ByteArray? {
        val decoded = path
            .replace("%2e", ".")
            .replace("%2E", ".")
            .replace("%2f", "/")
            .replace("%2F", "/")
            .replace("%5c", "/")
            .replace("%5C", "/")
        val normalized = if (documentDir.startsWith("content://")) {
            normalize(decoded)
        } else {
            normalize(join(documentDir, decoded))
        } ?: return null
        return loader(normalized)
    }

    companion object {
        /** 目录 + 相对路径 → POSIX 归一化路径；越界返回 null。 */
        fun join(documentDir: String, relative: String): String {
            val base = documentDir.trim('/')
            val combined = if (base.isEmpty()) relative.trim('/') else "$base/${relative.trim('/')}"
            return normalize(combined) ?: ""
        }

        /** POSIX 归一化：解析 ./ 与 ../；逃出根返回 null。 */
        fun normalize(path: String): String? {
            if (path.contains("\\") || path.contains("\u0000")) {
                return null
            }
            val stack = ArrayDeque<String>()
            for (segment in path.split('/')) {
                when (segment) {
                    "", "." -> continue
                    ".." -> {
                        if (stack.isEmpty()) {
                            return null
                        }
                        stack.removeLast()
                    }
                    else -> stack.addLast(segment)
                }
            }
            return stack.joinToString("/")
        }
    }
}
