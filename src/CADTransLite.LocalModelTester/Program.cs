// 本地模型可用性测试：直接调用 CADTransLite.Core 中的翻译器（与软件同一套代码路径），
// 自动拉起 Python 本地服务并加载模型，对每一个已安装的语言对方向做真实翻译探测。
// 运行位置必须为发布目录（publish/），以便 AppContext.BaseDirectory 能解析 tools/py 与 tools/bergamot。
using System.Diagnostics;
using CADTransLite.Core.Interfaces;
using CADTransLite.Core.Services;

var log = new List<string>();
int pass = 0, fail = 0;

// 清掉可能的代理环境变量，避免本地 127.0.0.1 请求或 Python 服务被代理干扰。
foreach (var k in new[] { "HTTP_PROXY", "HTTPS_PROXY", "http_proxy", "https_proxy" })
    Environment.SetEnvironmentVariable(k, "");

string baseDir = AppContext.BaseDirectory;
string bergRoot = Path.Combine(baseDir, "tools", "bergamot");

Console.WriteLine($"BaseDir = {baseDir}");
Console.WriteLine($"bergamotRoot exists = {Directory.Exists(bergRoot)}");
Console.WriteLine($"python.exe exists = {File.Exists(Path.Combine(baseDir, "tools", "py", "python.exe"))}");
Console.WriteLine();

// ---------- Bergamot（原生进程内引擎，无需 Python 服务） ----------
TestBergamot(bergRoot);

// ---------- LibreTranslate（本地 Python 服务） ----------
var lt = new LibreTranslateTranslator(new TranslationApiConfig { BaseUrl = "http://127.0.0.1:5000" });
TestHttp("LibreTranslate", lt, new[]
{
    ("en", "zh", "Hello world"),
    ("zh", "en", "你好世界"),
    ("en", "ja", "Hello world"),
});

// ---------- NLLB（本地 Python 服务） ----------
var nllb = new NllbTranslator(new TranslationApiConfig { BaseUrl = "http://127.0.0.1:5002" });
TestHttp("NLLB", nllb, new[]
{
    ("en", "zh", "Hello world"),
    ("zh", "en", "你好世界"),
});

// 清理本会话拉起的本地服务进程，释放端口。
LocalServerHelper.ShutdownAllOwned();

string summary = $"==== 本地模型测试结果 ====\nPASS={pass}  FAIL={fail}\n" + string.Join("\n", log);
File.WriteAllText(@"d:\local_model_test.log", summary);
Console.WriteLine();
Console.WriteLine(summary);
Environment.Exit(fail == 0 ? 0 : 1);

void TestBergamot(string root)
{
    if (!Directory.Exists(root))
    {
        Record("Bergamot", "FAIL", $"模型目录缺失: {root}");
        return;
    }

    var dirs = Directory.GetDirectories(root)
        .Where(d => File.Exists(Path.Combine(d, "config.txt")))
        .ToArray();
    Record("Bergamot", "INFO", $"发现 {dirs.Length} 个已安装语言对方向");

    var translator = new BergamotTranslator(root);
    foreach (var d in dirs)
    {
        var name = Path.GetFileName(d);
        var parts = name.Split('-');
        var src = parts[0];
        var tgt = parts[1];
        var sample = src == "en" ? "Hello world" : "你好世界";
        var sw = Stopwatch.StartNew();
        try
        {
            var warm = translator.WarmUp(src, tgt);
            if (warm != null)
            {
                sw.Stop();
                Record($"Bergamot {name}", "FAIL", $"WarmUp: {warm}  ({sw.ElapsedMilliseconds}ms)");
                continue;
            }

            var r = translator.TranslateAsync(sample, src, tgt).GetAwaiter().GetResult();
            sw.Stop();
            Record($"Bergamot {name}",
                string.IsNullOrWhiteSpace(r) ? "FAIL" : "PASS",
                string.IsNullOrWhiteSpace(r) ? $"空译文 ({sw.ElapsedMilliseconds}ms)" : $"{r}  ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Record($"Bergamot {name}", "FAIL", $"{ex.Message}  ({sw.ElapsedMilliseconds}ms)");
        }
    }
}

void TestHttp(string engine, ITranslationApi api, (string src, string tgt, string text)[] cases)
{
    foreach (var c in cases)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var r = api.TranslateAsync(c.text, c.src, c.tgt, CancellationToken.None).GetAwaiter().GetResult();
            sw.Stop();
            Record($"{engine} {c.src}->{c.tgt}",
                string.IsNullOrWhiteSpace(r) ? "FAIL" : "PASS",
                string.IsNullOrWhiteSpace(r) ? $"空译文 ({sw.ElapsedMilliseconds}ms)" : $"{r}  ({sw.ElapsedMilliseconds}ms)");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Record($"{engine} {c.src}->{c.tgt}", "FAIL", $"{ex.Message}  ({sw.ElapsedMilliseconds}ms)");
        }
    }
}

void Record(string id, string status, string detail)
{
    if (status == "PASS") pass++;
    else if (status == "FAIL") fail++;
    var line = $"[{status}] {id}: {detail}";
    log.Add(line);
    Console.WriteLine(line);
}
