<!DOCTYPE html>
<html lang="en-us">
<head>
    <meta charset="utf-8">
    <meta http-equiv="Content-Type" content="text/html; charset=utf-8">
    <title>@APPLICATION_NAME@</title>
    <script src="TemplateData/UnityProgress.js"></script>
    <script>
        // 允许不安全的 HTTP 连接
        var unityInstance = UnityLoader.instantiate("unityContainer", "@BUILD_URL@", {
            onProgress: UnityProgress,
            Module: {
                onRuntimeInitialized: function() {
                    // 允许混合内容（HTTP 请求）
                    UnityLoader.SystemInfo.hasWebGL ? 
                    UnityLoader.SystemInfo.hasWebGL = function() { return true; } : null;
                }
            }
        });
    </script>
</head>
<body>
    <div class="webgl-content">
        <div id="unityContainer" style="width: @WIDTH@px; height: @HEIGHT@px"></div>
    </div>
</body>
</html>
