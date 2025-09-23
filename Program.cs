using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QiCmd
{
    internal class Program
    {
        private static string _currentDirectory = Environment.CurrentDirectory;
        private static Dictionary<string, FunctionDefinition> _functions = new Dictionary<string, FunctionDefinition>(StringComparer.OrdinalIgnoreCase);

        static void Main(string[] args)
        {
            if (args.Length >= 1)
            {
                for (int i = 0; i < args.Length; ++i)
                {
                    string filePath = args[i];

                    if (File.Exists(filePath))
                        ProcessFile(filePath);

                    else
                        Console.WriteLine($"文件不存在: {filePath}");
                }
                return;
            }

            var calculator = new NumberCalculator();
            //Console.Clear();
            Console.WriteLine("QiCmd [版本 0.3]");
            //Console.WriteLine(calculator.CalculateFromString("(2 + 1)^3"));

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.Write($"\n{_currentDirectory}>");
                Console.ResetColor();
                string input = Console.ReadLine();

                if (string.IsNullOrWhiteSpace(input))
                    continue;

                if (input.Equals("exit", StringComparison.OrdinalIgnoreCase))
                    break;

                Run(input);
            }
        }

        private static void Run(string command)
        {
            string trimmedCommand = command.Trim();

            // 检查是否是函数调用（忽略前面的空格）
            if (TryExecuteFunction(trimmedCommand))
                return;

            string parsedCommand = QiCmdParser.ParseCommand(trimmedCommand);

            // 处理特殊命令
            if (TryHandleBuiltInCommand(parsedCommand))
                return;

            ExecuteCommand(parsedCommand);
        }

        public static void ProcessFile(string filePath)
        {
            try
            {
                // 读取文件的所有行
                string[] lines = File.ReadAllLines(filePath);

                // 逐行处理，支持多行函数定义
                ProcessLinesWithMultilineSupport(lines);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理文件时出错: {ex.Message}");
            }
        }

        private static void ProcessLinesWithMultilineSupport(string[] lines)
        {
            int i = 0;
            while (i < lines.Length)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(line))
                {
                    i++;
                    continue;
                }

                // 检查是否是函数定义开始
                if (line.StartsWith("def ", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("=> [") &&
                    !line.EndsWith("]"))
                {
                    // 多行函数定义 - 收集所有相关行
                    List<string> functionLines = new List<string> { line };

                    i++; // 移动到下一行
                    int bracketCount = CountBrackets(line);

                    while (i < lines.Length && bracketCount > 0)
                    {
                        string currentLine = lines[i].Trim();
                        functionLines.Add(currentLine);
                        bracketCount += CountBrackets(currentLine);

                        if (bracketCount <= 0)
                        {
                            // 括号匹配完成
                            i++;
                            break;
                        }

                        i++;
                    }

                    // 处理多行函数定义
                    ProcessMultilineFunctionDefinition(functionLines);
                }
                else
                {
                    // 普通行
                    Run(line);
                    i++;
                }
            }
        }

        private static void ProcessMultilineFunctionDefinition(List<string> functionLines)
        {
            if (functionLines.Count == 0) return;

            // 提取函数定义的第一行（包含 def 函数名 => [）
            string firstLine = functionLines[0];
            int arrowIndex = firstLine.IndexOf("=>");
            if (arrowIndex == -1) return;

            string functionName = firstLine.Substring(0, arrowIndex).Replace("def", "").Trim();
            if (string.IsNullOrEmpty(functionName)) return;

            // 提取命令部分（移除第一行的 [ 和最后一行的 ]）
            var commands = new List<string>();

            // 处理第一行（可能包含部分命令）
            string firstLineCommands = firstLine.Substring(arrowIndex + 2).Trim();
            if (firstLineCommands.StartsWith("["))
            {
                firstLineCommands = firstLineCommands.Substring(1).Trim();
            }

            if (!string.IsNullOrWhiteSpace(firstLineCommands))
            {
                commands.Add(firstLineCommands);
            }

            // 处理中间行
            for (int j = 1; j < functionLines.Count - 1; j++)
            {
                string cmd = functionLines[j].Trim();
                if (!string.IsNullOrWhiteSpace(cmd))
                {
                    commands.Add(cmd);
                }
            }

            // 处理最后一行（移除 ]）
            if (functionLines.Count > 1)
            {
                string lastLine = functionLines[functionLines.Count - 1].Trim();
                if (lastLine.EndsWith("]"))
                {
                    lastLine = lastLine.Substring(0, lastLine.Length - 1).Trim();
                }
                if (!string.IsNullOrWhiteSpace(lastLine))
                {
                    commands.Add(lastLine);
                }
            }

            // 创建函数定义
            if (commands.Count > 0)
            {
                var function = new FunctionDefinition
                {
                    Name = functionName,
                    Commands = commands.ToArray(),
                    IsSingleLine = false
                };
                _functions[functionName] = function;
            }
        }

        /// <summary>
        /// 计算字符串中的括号平衡情况
        /// </summary>
        private static int CountBrackets(string line)
        {
            int count = 0;
            foreach (char c in line)
            {
                if (c == '[') count++;
                else if (c == ']') count--;
            }
            return count;
        }

        /// <summary>
        /// 预处理多行函数定义，将多行合并为单行
        /// </summary>
        private static string PreprocessMultilineFunctions(string content)
        {
            // 匹配 def 函数名 => [ ... ] 的模式
            var pattern = @"(def\s+\w+\s*=>\s*\[)(.*?)(\])";

            return Regex.Replace(content, pattern, match =>
            {
                string defStart = match.Groups[1].Value;  // def 函数名 => [
                string commands = match.Groups[2].Value; // 命令内容
                string defEnd = match.Groups[3].Value;   // ]

                // 将多行命令合并为单行，用分号分隔
                string[] commandLines = commands.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(cmd => cmd.Trim())
                                               .Where(cmd => !string.IsNullOrWhiteSpace(cmd))
                                               .ToArray();

                string mergedCommands = string.Join("; ", commandLines);

                return defStart + mergedCommands + defEnd;
            }, RegexOptions.Singleline);
        }

        private static bool TryHandleBuiltInCommand(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            string cmd = parts[0].ToLower();

            switch (cmd)
            {
                case "cd":
                    HandleCdCommand(parts);
                    return true;

                case "chdir":
                    HandleCdCommand(parts);
                    return true;

                case "pwd":
                    Console.WriteLine(_currentDirectory);
                    return true;

                case "cls":
                    Console.Clear();
                    return true;

                case "echo":
                    if (parts.Length > 1)
                        Console.WriteLine(string.Join(" ", parts.Skip(1)));
                    else
                        Console.WriteLine();
                    return true;

                case "io": case "fe":
                    FileExplorer explorer = new FileExplorer(_currentDirectory);
                    explorer.Run();
                    return true;

                case "def":
                    HandleDefCommand(command);
                    return true;

                case "listfuncs":
                    ListFunctions();
                    return true;

                case "delfunc":
                    if (parts.Length > 1)
                        DeleteFunction(parts[1]);
                    else
                        Console.WriteLine("用法: delfunc [函数名]");
                    return true;

                default:
                    return false;
            }
        }

        private static void HandleDefCommand(string command)
        {
            try
            {
                // 移除开头的def和空格
                string defContent = command.Substring(3).Trim();

                // 解析函数定义
                var function = ParseFunctionDefinition(defContent);
                if (function != null)
                {
                    _functions[function.Name] = function;
                    //Console.WriteLine($"函数 '{function.Name}' 定义成功");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"定义函数时出错: {ex.Message}");
            }
        }

        private static void DeleteFunction(string functionName)
        {
            if (_functions.ContainsKey(functionName))
            {
                _functions.Remove(functionName);
                Console.WriteLine($"函数 '{functionName}' 已删除");
            }
            else
            {
                Console.WriteLine($"函数 '{functionName}' 不存在");
            }
        }

        private static FunctionDefinition ParseFunctionDefinition(string defContent)
        {
            // 查找 => 分隔符
            int arrowIndex = defContent.IndexOf("=>");
            if (arrowIndex == -1)
            {
                Console.WriteLine("错误: 函数定义必须包含 '=>' 分隔符");
                return null;
            }

            // 提取函数名
            string functionName = defContent.Substring(0, arrowIndex).Trim();
            if (string.IsNullOrWhiteSpace(functionName))
            {
                Console.WriteLine("错误: 函数名不能为空");
                return null;
            }

            // 提取命令部分
            string commandPart = defContent.Substring(arrowIndex + 2).Trim();

            // 检查是否是单行命令还是多行命令
            if (commandPart.StartsWith("["))
            {
                // 多行命令
                if (!commandPart.EndsWith("]"))
                {
                    Console.WriteLine("错误: 多行命令必须以 ']' 结束");
                    return null;
                }

                // 移除方括号
                string commandsText = commandPart.Substring(1, commandPart.Length - 2).Trim();

                // 分割命令（支持分号分隔或原来的多行格式）
                string[] commands = commandsText.Split(new[] { ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                               .Select(cmd => cmd.Trim())
                                               .Where(cmd => !string.IsNullOrWhiteSpace(cmd))
                                               .ToArray();

                if (commands.Length == 0)
                {
                    Console.WriteLine("错误: 函数必须包含至少一条命令");
                    return null;
                }

                return new FunctionDefinition
                {
                    Name = functionName,
                    Commands = commands,
                    IsSingleLine = false
                };
            }
            else
            {
                // 单行命令
                return new FunctionDefinition
                {
                    Name = functionName,
                    Commands = new[] { commandPart },
                    IsSingleLine = true
                };
            }
        }

        private static bool TryExecuteFunction(string command)
        {
            string functionName = command.Trim();

            if (_functions.ContainsKey(functionName))
            {
                var function = _functions[functionName];

                //OutDebugText($"执行函数: {function.Name}");

                foreach (string cmd in function.Commands)
                {
                    if (!string.IsNullOrWhiteSpace(cmd))
                    {
                        Run(cmd);
                    }
                }

                return true;
            }

            return false;
        }

        private static void ListFunctions()
        {
            if (_functions.Count == 0)
            {
                Console.WriteLine("没有定义任何函数");
                return;
            }

            Console.WriteLine("已定义的函数:");
            Console.WriteLine("════════════════════════════════════════════════════════════");

            foreach (var function in _functions.Values)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"{function.Name}");
                Console.ResetColor();
                Console.Write(" => ");

                if (function.IsSingleLine)
                {
                    Console.WriteLine(function.Commands[0]);
                }
                else
                {
                    Console.WriteLine("[");
                    foreach (string cmd in function.Commands)
                    {
                        Console.WriteLine($"  {cmd}");
                    }
                    Console.WriteLine("]");
                }
            }
        }

        private static void HandleCdCommand(string[] parts)
        {
            if (parts.Length == 1)
            {
                // 显示当前目录
                Console.WriteLine(_currentDirectory);
                return;
            }

            string targetPath = parts[1];

            try
            {
                string newPath = Path.GetFullPath(Path.Combine(_currentDirectory, targetPath));

                if (Directory.Exists(newPath))
                {
                    _currentDirectory = newPath;
                    Environment.CurrentDirectory = newPath; // 同时设置进程当前目录
                    Console.WriteLine($"当前目录已更改为: {_currentDirectory}");
                }
                else
                {
                    Console.WriteLine($"系统找不到指定的路径: {targetPath}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
        }

        private static void ExecuteCommand(string command)
        {
            if (IsInteractiveCommand(command))
            {
                // 交互式命令：直接执行，不重定向
                Process.Start("cmd.exe", $"/C {command}")?.WaitForExit();
            }
            else
            {
                // 普通命令：重定向输出
                Process process = new Process();
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/C {command}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = _currentDirectory
                };

                process.StartInfo = startInfo;
                process.OutputDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine(e.Data);
                        Console.ResetColor();
                    }
                };
                process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("ERROR: " + e.Data);
                        Console.ResetColor();
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
            }
        }

        private static bool IsInteractiveCommand(string command)
        {
            var interactiveCommands = new[] { "cmd", "powershell", "bash", "ssh", "telnet" };
            var firstWord = command.Split(' ')[0].ToLower();
            return interactiveCommands.Contains(firstWord);
        }
    }
    
    






    public class QiCmdParser
    {
        private const bool DeBugMode = false;

        // 时间单位转换字典
        private static readonly Dictionary<char, long> TimeUnits = new Dictionary<char, long>
        {
            {'s', 1}, {'S', 1},
            {'m', 60}, {'M', 60},
            {'h', 3600}, {'H', 3600},
            {'d', 86400}, {'D', 86400}
        };

        // 转换器注册表
        private static readonly Dictionary<string, Func<string, string>> Converters =
            new Dictionary<string, Func<string, string>>(StringComparer.OrdinalIgnoreCase)
            {
                // 类型默认转换器
                {"Number", value => {
                    try { return int.Parse(value).ToString(); }
                    catch { return value; }
                }},
                {"Time", value => {
                    // Time 转换器应该处理时间表达式，而不是日期字符串
                    try { return SecondsToTimeFormat(int.Parse(value)); }
                    catch { 
                        // 如果不是数字，检查是否是时间表达式
                        if (IsTimeExpression(value)) return value;
                        return value;
                    }
                }},
                {"String", value => value},
                {"Boolean", value => value.ToLower()},
                {"Date", value => {
                    try {
                        DateTime date;
                        if (DateTime.TryParse(value, out date))
                            return date.ToString("yyyy/MM/dd HH:mm:ss");
                        return value;
                    }
                    catch { return value; }
                }},
        
                // 类型特定操作转换器
                {"Number.Length", value => value.Length.ToString()},
                {"Number.Double", value => double.Parse(value).ToString()},
                {"Number.Abs", value => {
                    try
                    {
                        if (int.TryParse(value, out int number))
                            return Math.Abs(number).ToString();
                        if (double.TryParse(value, out double doubleNumber))
                            return Math.Abs(doubleNumber).ToString();
                        return value;
                    }
                    catch { return value; }
                }},
                {"Number.Neg", value => {
                    try
                    {
                        if (int.TryParse(value, out int number))
                            return (-number).ToString();
                        if (double.TryParse(value, out double doubleNumber))
                            return (-doubleNumber).ToString();
                        return value;
                    }
                    catch { return value; }
                }},
                {"Number.Round", value => {
                    try
                    {
                        if (double.TryParse(value, out double number))
                            return Math.Round(number).ToString();
                        return value;
                    }
                    catch { return value; }
                }},

                {"Time.Sec", value => ParseTimeToTotalSeconds(value).ToString()},
                {"Time.Min", value => ParseTimeToMinutes(value)},
                {"Time.Hour", value => ParseTimeToHours(value)},
                {"Time.Day", value => ParseTimeToDays(value)},

                {"String.Upper", value => value.ToUpper()},
                {"String.Lower", value => value.ToLower()},
                {"String.Length", value => value.Length.ToString()},
        
                // Boolean 操作转换器
                {"Boolean.Not", value => (!bool.Parse(value)).ToString().ToLower()},
                {"Boolean.Number", value => bool.Parse(value) ? "1" : "0"},
        
                // 类型间转换器
                {"Time.Number", value => ParseTimeToTotalSeconds(value).ToString()},
                {"Number.String", value => value},
                {"String.Number", value => double.TryParse(value, out _) ? value : "0"},

                // 关键的转换器：Date.Time（日期转时间差）
                // Date.Time 转换器
                {"Date.Time", value => {
                    try
                    {
                        DateTime targetDate;
                        if (DateTime.TryParse(value, out targetDate) ||
                            DateTime.TryParseExact(value, "yyyy/MM/dd HH:mm:ss", null,
                                System.Globalization.DateTimeStyles.None, out targetDate) ||
                            DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", null,
                                System.Globalization.DateTimeStyles.None, out targetDate))
                        {
                            // 忽略日期部分，只提取时间
                            TimeSpan timeOfDay = targetDate.TimeOfDay;

                            OutDebugText($"时间提取 - 原始: {targetDate}, 时间部分: {timeOfDay}");

                            // 转换为已过时间段格式 (15h6m32s)
                            return TimeSpanToTimeFormat(timeOfDay);
                        }
                        OutDebugText($"解析日期失败: {value}");
                        return value;
                    }
                    catch (Exception ex)
                    {
                        OutDebugText($"Date.Time error: {ex.Message}");
                        return value;
                    }
                }},
        
                // Date.Now 转换器
                {"Date.Now", value => {
                    try
                    {
                        TimeSpan timeSpan = ParseTimeToTimeSpan(value);
                        DateTime result = DateTime.Now.Add(timeSpan);
                        OutDebugText($"Date.Now - 输入: {value}, 时间跨度: {timeSpan}, 结果: {result}");
                        return result.ToString("yyyy/MM/dd HH:mm:ss");
                    }
                    catch (Exception ex)
                    {
                        OutDebugText($"Date.Now error: {ex.Message}");
                        return value;
                    }
                }},
                {"Date.UTC", value => {
                    try
                    {
                        TimeSpan timeSpan = ParseTimeToTimeSpan(value);
                        return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                            .Add(timeSpan).ToString("yyyy/MM/dd HH:mm:ss");
                    }
                    catch { return value; }
                }},
                {"Date.Today", value => {
                    try
                    {
                        TimeSpan timeSpan = ParseTimeToTimeSpan(value);
                        return DateTime.Today.Add(timeSpan).ToString("yyyy/MM/dd HH:mm:ss");
                    }
                    catch { return value; }
                }}
            };

        /// <summary>
        /// 解析命令字符串，转换所有表达式
        /// </summary>
        public static string ParseCommand(string inputCommand)
        {
            if (string.IsNullOrWhiteSpace(inputCommand))
                return inputCommand;

            // 处理管道表达式：支持显式 $[Type:Value => ...] 和隐式 $[?:Value => ...]
            const string pipelinePattern = @"\$\[\s*(\??\w*|@)\s*:\s*([^=\]]+?)(?:\s*=>\s*([^\]]+))?\s*\]";
            return Regex.Replace(inputCommand, pipelinePattern, match =>
            {
                string typeName = match.Groups[1].Value;
                string value = match.Groups[2].Value.Trim();
                string pipeline = match.Groups[3].Success ? match.Groups[3].Value.Trim() : null;

                // 处理 @ 开头的生成器表达式
                if (typeName == "@")
                {
                    return ParseGeneratorExpression(value, pipeline);
                }

                // 处理隐式类型（? 或空类型）
                if (string.IsNullOrEmpty(typeName) || typeName == "?")
                {
                    typeName = DetectValueType(value);
                }

                return ParsePipelineExpression(typeName, value, pipeline);
            });
        }

        /// <summary>
        /// 智能检测值的类型
        /// </summary>
        private static string DetectValueType(string value)
        {
            value = value.Trim();

            // 检测日期时间格式
            DateTime dateResult;
            if (DateTime.TryParse(value, out dateResult))
            {
                return "Date";
            }

            // 原有的检测逻辑...
            if (Regex.IsMatch(value, @"^\d+[smhdSMHD](\d+[smhdSMHD])*$") ||
                Regex.IsMatch(value, @"^\d+[smhdSMHD]?$"))
            {
                return "Time";
            }

            if (int.TryParse(value, out _) || double.TryParse(value, out _))
            {
                return "Number";
            }

            if (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                return "Boolean";
            }

            return "String";
        }

        private static string ParseGeneratorExpression(string functionCall, string pipeline)
        {
            // 解析函数名和参数
            var match = Regex.Match(functionCall, @"(\w+)\(([^)]*)\)");
            if (!match.Success)
                return functionCall; // 不是有效的函数调用

            string functionName = match.Groups[1].Value;
            string argsString = match.Groups[2].Value;
            string[] args = argsString.Split(',').Select(arg => arg.Trim()).ToArray();

            string generatedValue = GenerateValue(functionName, args);

            // 如果有后续管道，继续处理
            if (!string.IsNullOrEmpty(pipeline))
            {
                return ParsePipelineExpression(DetectValueType(generatedValue), generatedValue, pipeline);
            }

            return generatedValue;
        }

        private static string GenerateValue(string functionName, string[] args)
        {
            switch (functionName.ToLower())
            {
                // 时间与日期
                case "gettime":
                    if (args.Length == 1 && args[0].Equals("Now", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime now = DateTime.Now;
                        return $"{now.Hour}h{now.Minute}m{now.Second}s";
                    }
                    break;
                case "getdate":
                    if (args.Length == 1 && args[0].Equals("Now", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime now = DateTime.Now;
                        return $"{now.Date.Year}/{now.Date.Month}/{now.Date.Day} {now.Hour}:{now.Minute}:{now.Second}";
                    }
                    break;

                // 
                case "ifeo":
                    if (args.Length == 2)
                    {
                        return $"reg add \"HKLM\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\{args[0]}\" /v Debugger /t REG_SZ /d \"{args[1]}\" /f";
                    }
                    break;
            }

            return $"[Error: Unknown function '{functionName}']";
        }

        /// <summary>
        /// 解析管道表达式
        /// </summary>
        private static string ParsePipelineExpression(string initialType, string initialValue, string pipeline)
        {
            string currentValue = initialValue;
            string currentType = initialType;

            OutDebugText($"开始 {initialType}:{initialValue} => {pipeline}");

            if (string.IsNullOrWhiteSpace(pipeline))
            {
                if (Converters.TryGetValue(currentType, out var converter))
                {
                    return converter(currentValue);
                }
                return currentValue;
            }

            var steps = pipeline.Split(new[] { "=>" }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(step => step.Trim())
                               .Where(step => !string.IsNullOrEmpty(step))
                               .ToList();

            foreach (string step in steps)
            {
                OutDebugText($"步骤 '{step}', 当前: {currentType}:{currentValue}");

                // 对于 Date => Time 这种情况，我们需要使用 Date.Time 转换器
                if (currentType == "Date" && step == "Time")
                {
                    string converterKey = "Date.Time";
                    OutDebugText($"使用特定转换器: {converterKey}");

                    if (Converters.TryGetValue(converterKey, out var converter))
                    {
                        try
                        {
                            currentValue = converter(currentValue);
                            currentType = "Time";
                            OutDebugText($"转换为: {currentValue} (类型: {currentType})");
                            continue;
                        }
                        catch (Exception ex)
                        {
                            OutDebugText($"转换失败: {ex.Message}");
                            return currentValue;
                        }
                    }
                }

                // 查找完整的转换器键名
                if (step.Contains('.'))
                {
                    string converterKey = step;
                    OutDebugText($"寻找转换器: {converterKey}");

                    if (Converters.TryGetValue(converterKey, out var converter))
                    {
                        try
                        {
                            currentValue = converter(currentValue);
                            currentType = step.Split('.')[0];
                            OutDebugText($"转换为: {currentValue} (类型: {currentType})");
                            continue;
                        }
                        catch (Exception ex)
                        {
                            OutDebugText($"转换失败: {ex.Message}");
                            return currentValue;
                        }
                    }
                }

                // 查找类型默认转换器
                string typeConverterKey = step;
                OutDebugText($"寻找类型转换器: {typeConverterKey}");

                if (Converters.TryGetValue(typeConverterKey, out var typeConverter))
                {
                    try
                    {
                        currentValue = typeConverter(currentValue);
                        currentType = step;
                        OutDebugText($"转换为: {currentValue} (类型: {currentType})");
                    }
                    catch (Exception ex)
                    {
                        OutDebugText($"类型转换失败: {ex.Message}");
                        return currentValue;
                    }
                }
                else
                {
                    OutDebugText($"类型未找到: {typeConverterKey}");
                    return currentValue;
                }
            }

            return currentValue;
        }


        // 时间转换方法
        private static string ParseTimeToMinutes(string timeExpression)
        {
            long totalSeconds = ParseTimeToTotalSeconds(timeExpression);
            double minutes = (double)totalSeconds / 60;
            return Math.Round(minutes).ToString();
        }

        private static string ParseTimeToHours(string timeExpression)
        {
            long totalSeconds = ParseTimeToTotalSeconds(timeExpression);
            double hours = (double)totalSeconds / 3600;
            return Math.Round(hours).ToString();
        }

        private static string ParseTimeToDays(string timeExpression)
        {
            long totalSeconds = ParseTimeToTotalSeconds(timeExpression);
            double days = (double)totalSeconds / 86400;
            return Math.Round(days).ToString();
        }

        private static long ParseTimeToTotalSeconds(string timeExpression)
        {
            timeExpression = timeExpression.Trim().ToLower();

            if (int.TryParse(timeExpression, out var seconds))
            {
                return seconds;
            }

            long totalSeconds = 0;
            var matches = Regex.Matches(timeExpression, @"(\d+)([smhd])");

            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    var number = int.Parse(match.Groups[1].Value);
                    var unit = match.Groups[2].Value;

                    if (TimeUnits.TryGetValue(unit[0], out var multiplier))
                    {
                        totalSeconds += number * multiplier;
                    }
                }
            }

            return totalSeconds;
        }

        // 将秒数转换为时间格式
        private static string SecondsToTimeFormat(int totalSeconds)
        {
            if (totalSeconds == 0)
                return "0s";

            int remainingSeconds = Math.Abs(totalSeconds);
            var timeParts = new List<string>();

            // 天数
            int days = remainingSeconds / 86400;
            if (days > 0)
            {
                timeParts.Add($"{days}d");
                remainingSeconds %= 86400;
            }

            // 小时
            int hours = remainingSeconds / 3600;
            if (hours > 0)
            {
                timeParts.Add($"{hours}h");
                remainingSeconds %= 3600;
            }

            // 分钟
            int minutes = remainingSeconds / 60;
            if (minutes > 0)
            {
                timeParts.Add($"{minutes}m");
                remainingSeconds %= 60;
            }

            // 秒
            if (remainingSeconds > 0)
            {
                timeParts.Add($"{remainingSeconds}s");
            }

            return string.Join("", timeParts);
        }

        /// <summary>
        /// 将时间表达式转换为 TimeSpan
        /// </summary>
        private static TimeSpan ParseTimeToTimeSpan(string timeExpression)
        {
            long totalSeconds = ParseTimeToTotalSeconds(timeExpression);
            return TimeSpan.FromSeconds(totalSeconds);
        }

        /// <summary>
        /// 判断是否为时间表达式
        /// </summary>
        private static bool IsTimeExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                return false;

            expression = expression.Trim();

            // 纯数字（表示秒数）
            if (int.TryParse(expression, out _))
                return true;

            // 时间格式：1h, 30m, 2d10h30m, 1h20m30s 等
            if (Regex.IsMatch(expression, @"^(\d+[smhdSMHD])+$"))
                return true;

            // 带数字的时间格式：1h, 30m 等（单个单位）
            if (Regex.IsMatch(expression, @"^\d+[smhdSMHD]?$"))
                return true;

            return false;
        }

        // 将 TimeSpan 转换为时间格式 (15h6m32s)
        private static string TimeSpanToTimeFormat(TimeSpan timeSpan)
        {
            if (timeSpan.TotalSeconds == 0)
                return "0s";

            var timeParts = new List<string>();

            // 小时
            int hours = timeSpan.Hours;
            if (hours > 0)
            {
                timeParts.Add($"{hours}h");
            }

            // 分钟
            int minutes = timeSpan.Minutes;
            if (minutes > 0)
            {
                timeParts.Add($"{minutes}m");
            }

            // 秒
            int seconds = timeSpan.Seconds;
            if (seconds > 0)
            {
                timeParts.Add($"{seconds}s");
            }

            return string.Join("", timeParts);
        }

        private static void OutDebugText(string message)
        {
            if(DeBugMode)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"DEBUG: {message}");
                Console.ResetColor();
            }
        }
    }









    // 函数定义类
    public class FunctionDefinition
    {
        public string Name { get; set; }
        public string[] Commands { get; set; }
        public bool IsSingleLine { get; set; }
    }



    /// <summary>
    /// 数字计算器类，支持加减乘除和幂运算
    /// </summary>
    public class NumberCalculator
    {
        // 运算符优先级字典
        private static readonly Dictionary<char, int> OperatorPrecedence = new Dictionary<char, int>
        {
            { '+', 1 },
            { '-', 1 },
            { '*', 2 },
            { '/', 2 },
            { '^', 3 }  // 幂运算优先级最高
        };

        /// <summary>
        /// 执行数学运算
        /// </summary>
        public double Calculate(double num1, double num2, char operation)
        {
            switch (operation)
            {
                case '+':
                    return Add(num1, num2);
                case '-':
                    return Subtract(num1, num2);
                case '*':
                    return Multiply(num1, num2);
                case '/':
                    return Divide(num1, num2);
                case '^':
                    return Power(num1, num2);
                default:
                    throw new ArgumentException($"不支持的运算符: {operation}");
            }
        }

        /// <summary>
        /// 加法运算
        /// </summary>
        public double Add(double num1, double num2) => num1 + num2;

        /// <summary>
        /// 减法运算
        /// </summary>
        public double Subtract(double num1, double num2) => num1 - num2;

        /// <summary>
        /// 乘法运算
        /// </summary>
        public double Multiply(double num1, double num2) => num1 * num2;

        /// <summary>
        /// 除法运算
        /// </summary>
        public double Divide(double num1, double num2)
        {
            if (Math.Abs(num2) < double.Epsilon)
                throw new DivideByZeroException("除数不能为零");
            return num1 / num2;
        }

        /// <summary>
        /// 幂运算
        /// </summary>
        public double Power(double baseNumber, double exponent)
        {
            if (Math.Abs(baseNumber) < double.Epsilon && exponent < 0)
                throw new ArgumentException("0的负数次方无定义");
            if (baseNumber < 0 && exponent % 1 != 0)
                throw new ArgumentException("负数的非整数次方无定义");
            return Math.Pow(baseNumber, exponent);
        }

        /// <summary>
        /// 使用调度场算法解析并计算表达式
        /// </summary>
        public double CalculateFromString(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("表达式不能为空");

            // 预处理：移除所有空格
            expression = new string(expression.Where(c => !char.IsWhiteSpace(c)).ToArray());

            var outputQueue = new Queue<string>();
            var operatorStack = new Stack<char>();

            int i = 0;
            while (i < expression.Length)
            {
                char currentChar = expression[i];

                if (char.IsDigit(currentChar) || currentChar == '.')
                {
                    // 解析数字
                    string number = ParseNumber(expression, ref i);
                    outputQueue.Enqueue(number);
                }
                else if (IsOperator(currentChar))
                {
                    // 处理运算符
                    while (operatorStack.Count > 0 && IsOperator(operatorStack.Peek()) &&
                           ((OperatorPrecedence[operatorStack.Peek()] > OperatorPrecedence[currentChar]) ||
                            (OperatorPrecedence[operatorStack.Peek()] == OperatorPrecedence[currentChar] && currentChar != '^')) &&
                           operatorStack.Peek() != '(')
                    {
                        outputQueue.Enqueue(operatorStack.Pop().ToString());
                    }
                    operatorStack.Push(currentChar);
                    i++;
                }
                else if (currentChar == '(')
                {
                    operatorStack.Push(currentChar);
                    i++;
                }
                else if (currentChar == ')')
                {
                    while (operatorStack.Count > 0 && operatorStack.Peek() != '(')
                    {
                        outputQueue.Enqueue(operatorStack.Pop().ToString());
                    }

                    if (operatorStack.Count == 0)
                        throw new ArgumentException("括号不匹配");

                    operatorStack.Pop(); // 弹出 '('
                    i++;
                }
                else
                {
                    throw new ArgumentException($"无效字符: {currentChar}");
                }
            }

            // 将栈中剩余运算符弹出
            while (operatorStack.Count > 0)
            {
                if (operatorStack.Peek() == '(')
                    throw new ArgumentException("括号不匹配");
                outputQueue.Enqueue(operatorStack.Pop().ToString());
            }

            // 计算后缀表达式
            return EvaluatePostfix(outputQueue);
        }

        /// <summary>
        /// 解析数字（支持整数和小数）
        /// </summary>
        private string ParseNumber(string expression, ref int index)
        {
            int start = index;
            bool hasDecimalPoint = false;

            while (index < expression.Length)
            {
                char c = expression[index];
                if (char.IsDigit(c))
                {
                    index++;
                }
                else if (c == '.' && !hasDecimalPoint)
                {
                    hasDecimalPoint = true;
                    index++;
                }
                else
                {
                    break;
                }
            }

            return expression.Substring(start, index - start);
        }

        /// <summary>
        /// 判断是否为支持的运算符
        /// </summary>
        private bool IsOperator(char c)
        {
            return OperatorPrecedence.ContainsKey(c);
        }

        /// <summary>
        /// 计算后缀表达式
        /// </summary>
        private double EvaluatePostfix(Queue<string> postfixQueue)
        {
            var stack = new Stack<double>();
            var queue = new Queue<string>(postfixQueue); // 创建副本

            while (queue.Count > 0)
            {
                string token = queue.Dequeue();

                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    stack.Push(number);
                }
                else if (token.Length == 1 && IsOperator(token[0]))
                {
                    if (stack.Count < 2)
                        throw new ArgumentException("表达式无效");

                    double num2 = stack.Pop();
                    double num1 = stack.Pop();
                    double result = Calculate(num1, num2, token[0]);
                    stack.Push(result);
                }
                else
                {
                    throw new ArgumentException($"无效的令牌: {token}");
                }
            }

            if (stack.Count != 1)
                throw new ArgumentException("表达式无效");

            return stack.Pop();
        }

        /// <summary>
        /// 简单的单运算符表达式计算（向后兼容）
        /// </summary>
        public double CalculateSimpleExpression(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression))
                throw new ArgumentException("表达式不能为空");

            // 支持的操作符
            char[] operators = { '+', '-', '*', '/', '^' };
            int operatorIndex = -1;
            char operation = ' ';

            // 查找操作符位置（处理负号情况）
            for (int i = 0; i < expression.Length; i++)
            {
                char currentChar = expression[i];
                if (operators.Contains(currentChar))
                {
                    // 检查是否是负号而不是减号
                    if (currentChar == '-' && (i == 0 || expression[i - 1] == ' ' ||
                        operators.Contains(expression[i - 1])))
                    {
                        continue; // 跳过负号情况
                    }

                    operatorIndex = i;
                    operation = currentChar;
                    break;
                }
            }

            if (operatorIndex == -1)
                throw new ArgumentException($"表达式中未找到有效的运算符。支持的运算符: {string.Join(", ", operators)}");

            // 分割字符串
            string num1Str = expression.Substring(0, operatorIndex).Trim();
            string num2Str = expression.Substring(operatorIndex + 1).Trim();

            if (double.TryParse(num1Str, NumberStyles.Float, CultureInfo.InvariantCulture, out double num1) &&
                double.TryParse(num2Str, NumberStyles.Float, CultureInfo.InvariantCulture, out double num2))
            {
                return Calculate(num1, num2, operation);
            }
            else
            {
                throw new ArgumentException("无法解析表达式中的数字");
            }
        }

        /// <summary>
        /// 显示支持的所有运算符和示例
        /// </summary>
        public void DisplaySupportedOperations()
        {
            Console.WriteLine("支持的运算:");
            Console.WriteLine("+ : 加法 (示例: 5 + 3 = 8)");
            Console.WriteLine("- : 减法 (示例: 10 - 4 = 6)");
            Console.WriteLine("* : 乘法 (示例: 6 * 7 = 42)");
            Console.WriteLine("/ : 除法 (示例: 15 / 3 = 5)");
            Console.WriteLine("^ : 幂运算 (示例: 2 ^ 3 = 8)");
            Console.WriteLine("支持复杂表达式如: (3 + 2) * 4, 3 ^ 2 + 1, 10 / 2 + 3 * 2");
            Console.WriteLine();
        }
    }

    

















    public class FileExplorer
    {
        private string currentDirectory;
        private List<FileSystemInfo> currentItems;
        private int currentPage = 1;
        private const int PageSize = 50 + 1;

        public FileExplorer(string startDirectory = null)
        {
            currentDirectory = startDirectory ?? Directory.GetCurrentDirectory();
            currentItems = new List<FileSystemInfo>();
        }

        public void Run()
        {
            while (true)
            {
                try
                {
                    RefreshDisplay();
                    if (HandleInput() == "exit")
                        break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"错误: {ex.Message}");
                    Console.WriteLine("按任意键继续...");
                    Console.ReadKey();
                }
            }
        }

        private void RefreshDisplay()
        {
            Console.Clear();
            LoadCurrentDirectoryItems();
            DisplayHeader();
            DisplayFileList();
            DisplayFooter();
        }

        private void LoadCurrentDirectoryItems()
        {
            currentItems.Clear();

            // 添加父目录（如果不是根目录）
            if (Directory.GetParent(currentDirectory) != null)
            {
                currentItems.Add(new DirectoryInfo(".."));
            }

            // 添加目录
            var directories = Directory.GetDirectories(currentDirectory)
                .Select(path => new DirectoryInfo(path))
                .OrderBy(d => d.Name);
            currentItems.AddRange(directories);

            // 添加文件
            var files = Directory.GetFiles(currentDirectory)
                .Select(path => new FileInfo(path))
                .OrderBy(f => f.Name);
            currentItems.AddRange(files);
        }

        private void DisplayHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ 文件资源管理器 - {TruncateMiddle(currentDirectory, 50)} ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  序号  类型      大小          修改时间              名称");
            Console.WriteLine("════════════════════════════════════════════════════════════");
            Console.ResetColor();
        }

        private void DisplayFileList()
        {
            int startIndex = (currentPage - 1) * PageSize;
            int endIndex = Math.Min(startIndex + PageSize, currentItems.Count);

            for (int i = startIndex; i < endIndex; i++)
            {
                var item = currentItems[i];
                int displayNumber = i;

                if (item.Name == "..")
                {
                    DisplayBackItem(displayNumber);
                }
                else if (item is DirectoryInfo dir)
                {
                    DisplayDirectoryItem(displayNumber, dir);
                }
                else if (item is FileInfo file)
                {
                    DisplayFileItem(displayNumber, file);
                }
            }
        }

        private void DisplayBackItem(int number)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write($"[{number,2}] ");
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.Write("⬆ 返回  ");
            Console.ResetColor();
            Console.WriteLine("上级目录");
        }

        private void DisplayDirectoryItem(int number, DirectoryInfo dir)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"[{number,2}] ");
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.Write("📁 目录  ");
            Console.ResetColor();
            Console.Write($"{GetLastWriteTime(dir.LastWriteTime),-20}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine($" {dir.Name}");
            Console.ResetColor();
        }

        private void DisplayFileItem(int number, FileInfo file)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"[{number,2}] ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("📄 文件  ");
            Console.Write($"{GetFileSize(file.Length),-12}");
            Console.Write($"{GetLastWriteTime(file.LastWriteTime),-20}");

            SetFileColor(file.Extension);
            Console.WriteLine($" {file.Name}");
            Console.ResetColor();
        }

        private void SetFileColor(string extension)
        {
            switch (extension.ToLower())
            {
                case ".txt":
                case ".md":
                case ".log":
                    Console.ForegroundColor = ConsoleColor.White;
                    break;
                case ".exe":
                case ".bat":
                case ".cmd":
                    Console.ForegroundColor = ConsoleColor.Red;
                    break;
                case ".jpg":
                case ".png":
                case ".gif":
                case ".bmp":
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    break;
                case ".mp3":
                case ".wav":
                case ".flac":
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    break;
                case ".mp4":
                case ".avi":
                case ".mkv":
                    Console.ForegroundColor = ConsoleColor.DarkMagenta;
                    break;
                case ".zip":
                case ".rar":
                case ".7z":
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Gray;
                    break;
            }
        }

        private void DisplayFooter()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("════════════════════════════════════════════════════════════");

            int totalPages = (int)Math.Ceiling((double)currentItems.Count / PageSize);
            if (totalPages > 1)
            {
                Console.WriteLine($"第 {currentPage}/{totalPages} 页 | ");
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("命令: [数字]选择 | q 上一页 | p 下一页 | e 退出");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("════════════════════════════════════════════════════════════");
            Console.Write("输入: ");
            Console.ResetColor();
        }

        private string HandleInput()
        {
            string input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input)) return "";

            switch (input.ToLower())
            {
                case "q":
                    if (currentPage > 1) currentPage--;
                    break;
                case "p":
                    if (currentPage < (int)Math.Ceiling((double)currentItems.Count / PageSize)) currentPage++;
                    break;
                case "e":
                    return "exit";
                case "..":
                    NavigateToParent();
                    break;
                default:
                    if (int.TryParse(input, out int selection))
                    {
                        HandleSelection(selection);
                    }
                    break;
            }
            return input.ToLower();
        }

        private void HandleSelection(int selection)
        {
            if (selection >= 0 && selection < currentItems.Count)
            {
                var selectedItem = currentItems[selection];

                if (selectedItem.Name == "..")
                {
                    NavigateToParent();
                }
                else if (selectedItem is DirectoryInfo dir)
                {
                    currentDirectory = dir.FullName;
                    currentPage = 1;
                }
                else if (selectedItem is FileInfo file)
                {
                    ShowFileMenu(file);
                }
            }
        }

        private void ShowFileMenu(FileInfo file)
        {
            while (true)
            {
                Console.Clear();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
                Console.WriteLine($"║ 文件操作: {TruncateMiddle(file.Name, 50)} ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine();

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("  0. 返回文件列表");
                Console.WriteLine("  1. 打开文件");
                Console.WriteLine("  2. 删除文件");
                Console.WriteLine("  3. 重命名文件");
                Console.WriteLine("  4. 查看文件内容");
                Console.ResetColor();

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("请选择操作 (0-4): ");
                Console.ResetColor();

                string input = Console.ReadLine()?.Trim();

                switch (input)
                {
                    case "0":
                        return; // 返回文件列表
                    case "1":
                        OpenFile(file);
                        break;
                    case "2":
                        DeleteFile(file);
                        break;
                    case "3":
                        RenameFile(file);
                        break;
                    case "4":
                        ViewFileContent(file);
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("无效的选择，请重新输入");
                        Console.ResetColor();
                        Console.WriteLine("按任意键继续...");
                        Console.ReadKey();
                        break;
                }
            }
        }

        private void OpenFile(FileInfo file)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ 打开文件: {TruncateMiddle(file.Name, 50)} ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = file.FullName,
                    UseShellExecute = true
                });

                Console.WriteLine("文件已用默认程序打开");
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"无法打开文件: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n按任意键返回...");
            Console.ReadKey();
        }

        private void DeleteFile(FileInfo file)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ 删除文件: {TruncateMiddle(file.Name, 50)} ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("确认要删除这个文件吗？(y/N): ");
            Console.ResetColor();

            string confirm = Console.ReadLine()?.Trim().ToLower();
            if (confirm == "y" || confirm == "yes")
            {
                try
                {
                    file.Delete();
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("文件已成功删除");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"删除文件失败: {ex.Message}");
                    Console.ResetColor();
                }
            }
            else
            {
                Console.WriteLine("取消删除操作");
            }

            Console.WriteLine("\n按任意键返回...");
            Console.ReadKey();
        }

        private void RenameFile(FileInfo file)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ 重命名文件: {TruncateMiddle(file.Name, 50)} ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            Console.Write($"请输入新文件名 (当前: {file.Name}): ");
            string newName = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(newName))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("文件名不能为空");
                Console.ResetColor();
            }
            else
            {
                try
                {
                    string newPath = Path.Combine(file.DirectoryName, newName);
                    file.MoveTo(newPath);
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("文件重命名成功");
                    Console.ResetColor();
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"重命名失败: {ex.Message}");
                    Console.ResetColor();
                }
            }

            Console.WriteLine("\n按任意键返回...");
            Console.ReadKey();
        }

        private void ViewFileContent(FileInfo file)
        {
            Console.Clear();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"╔══════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║ 查看文件内容: {TruncateMiddle(file.Name, 50)} ║");
            Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine();

            try
            {
                // 检查文件大小，避免读取过大文件
                if (file.Length > 1024 * 1024) // 1MB
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("文件较大，是否继续查看？(y/N): ");
                    Console.ResetColor();
                    string confirm = Console.ReadLine()?.Trim().ToLower();
                    if (confirm != "y" && confirm != "yes")
                    {
                        return;
                    }
                }

                // 尝试以文本格式读取文件
                using (var reader = new StreamReader(file.FullName))
                {
                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine("════════════════════════════════════════════════════════════");
                    Console.ResetColor();

                    string line;
                    int lineCount = 0;
                    while ((line = reader.ReadLine()) != null && lineCount < 1000) // 限制显示1000行
                    {
                        Console.WriteLine(line);
                        lineCount++;

                        // 每50行暂停一次
                        if (lineCount % 50 == 0)
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine($"--- 已显示 {lineCount} 行，按任意键继续，按q退出查看 ---");
                            Console.ResetColor();
                            if (Console.ReadKey(true).KeyChar == 'q')
                                break;
                        }
                    }

                    if (lineCount >= 1000)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("--- 文件内容较多，已截断显示 ---");
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"读取文件失败: {ex.Message}");
                Console.ResetColor();
            }

            Console.WriteLine("\n按任意键返回...");
            Console.ReadKey();
        }

        private void NavigateToParent()
        {
            var parent = Directory.GetParent(currentDirectory);
            if (parent != null)
            {
                currentDirectory = parent.FullName;
                currentPage = 1;
            }
        }

        // 辅助方法
        private string GetFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private string GetLastWriteTime(DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm");
        }

        private string TruncateMiddle(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value.PadRight(maxLength);

            int halfLength = (maxLength - 3) / 2;
            return value.Substring(0, halfLength) + "..." + value.Substring(value.Length - halfLength);
        }
    }
}
