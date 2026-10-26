package com.markleaf.app.data

/**
 * 标签活跃序（LRU）：上限 6 个活跃 WebView（spec §2 决策 2）。
 * 纯状态类，JVM 直测；WebView 容器层按 lruOrder() 增删实例。
 */
class TabState {

    companion object {
        const val MAX_ACTIVE = 6
    }

    private val order = ArrayDeque<String>()
    var activeId: String? = null
        private set

    fun lruOrder(): List<String> = order.toList()

    val count: Int get() = order.size

    /**
     * 打开新标签；超上限时移除最旧（返回其 id 供容器销毁 WebView），返回 null 表示未淘汰。
     */
    fun open(id: String): String? {
        if (order.contains(id)) {
            activate(id)
            return null
        }
        var evicted: String? = null
        if (order.size >= MAX_ACTIVE) {
            evicted = order.removeFirst()
        }
        order.addLast(id)
        activeId = id
        return evicted
    }

    fun activate(id: String) {
        if (!order.contains(id)) {
            return
        }
        order.remove(id)
        order.addLast(id)
        activeId = id
    }

    /** 关闭标签；最后一个标签不可关（应用保留空壳）。 */
    fun close(id: String): Boolean {
        if (order.size <= 1 || !order.contains(id)) {
            return false
        }
        order.remove(id)
        if (activeId == id) {
            activeId = order.lastOrNull()
        }
        return true
    }

    /** 低内存：只保留当前标签（spec §2 决策 2）。返回被移除的 id 列表。 */
    fun trimToActive(): List<String> {
        val active = activeId ?: return emptyList()
        val removed = order.filter { it != active }
        order.clear()
        order.addLast(active)
        return removed
    }
}
