# kotlinx.serialization：保留序列化器生成的伴生成员
-keepattributes *Annotation*, InnerClasses
-dontnote kotlinx.serialization.**
-keepclassmembers class com.markleaf.app.** {
    *** Companion;
}
-keepclasseswithmembers class com.markleaf.app.** {
    kotlinx.serialization.KSerializer serializer(...);
}
# WebView JS 桥
-keepclassmembers class com.markleaf.app.bridge.BridgeReceiver {
    @android.webkit.JavascriptInterface <methods>;
}
