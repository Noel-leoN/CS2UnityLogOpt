// Copyright (c) 2024 Noel2(Noel-leoN)
// Licensed under the MIT License.
// See LICENSE in the project root for full license information.
// When using this part of the code, please clearly credit [Project Name] and the author.

using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace CS2UnityLogOpt
{
    public static class Patcher
    {
        // 指定只修补核心模块
        public static IEnumerable<string> TargetDLLs { get; } = new[] { "UnityEngine.CoreModule.dll" };

        public static void Patch(AssemblyDefinition assembly)
        {
            var module = assembly.MainModule;

            // 1. 定位到底层处理类 DebugLogHandler
            var logHandlerType = module.Types.FirstOrDefault(t => t.Name == "DebugLogHandler");
            if (logHandlerType == null) return;

            // 2. 定位 LogFormat 方法
            // 签名: void LogFormat(LogType logType, UnityEngine.Object context, string format, params object[] args)
            var logMethod = logHandlerType.Methods.FirstOrDefault(m => m.Name == "LogFormat");
            if (logMethod == null) return;

            // 3. 获取我们要注入的过滤方法的引用
            // 注意：这里引用了本 DLL 中的 LogFilter 类，游戏运行时必须能加载此 DLL
            var filterMethod = module.ImportReference(typeof(LogFilter).GetMethod(nameof(LogFilter.ShouldSkipLog)));

            // 4. 开始修改 IL
            var il = logMethod.Body.GetILProcessor();
            var instructions = logMethod.Body.Instructions;
            var originalStart = instructions.First();

            /*
             * 我们要注入的逻辑等价于：
             * if (logType == LogType.Warning && LogFilter.ShouldSkipLog(format)) {
             *     return;
             * }
             * [原始代码...]
             */

            // 这里的参数索引 (对于实例方法):
            // arg0: this
            // arg1: logType (LogType Enum)
            // arg2: context (Object)
            // arg3: format (String) <--- 我们主要检查这个

            // 定义跳转标签
            var jumpToOriginal = originalStart;

            // --- 注入指令开始 ---

            // 1. 检查 LogType 是否为 Warning (值为2)
            var insLoadLogType = il.Create(OpCodes.Ldarg_1); // Load logType
            var insLoadWarningVal = il.Create(OpCodes.Ldc_I4_2); // Load 2
            var insBranchIfNotWarning = il.Create(OpCodes.Bne_Un, jumpToOriginal); // If != 2, goto Original

            // 2. 只有是 Warning 时，才调用过滤函数检查 String
            var insLoadFormatString = il.Create(OpCodes.Ldarg_3); // Load format (string)
            var insCallFilter = il.Create(OpCodes.Call, filterMethod); // Call ShouldSkipLog(string)
            var insBranchIfFalse = il.Create(OpCodes.Brfalse, jumpToOriginal); // If result is false, goto Original

            // 3. 如果是 Warning 且 Filter 返回 true，直接返回 (拦截)
            var insReturn = il.Create(OpCodes.Ret);

            // 插入顺序 (InsertBefore 会将新指令插在目标之前，所以按逻辑顺序写即可)
            il.InsertBefore(originalStart, insLoadLogType);
            il.InsertAfter(insLoadLogType, insLoadWarningVal);
            il.InsertAfter(insLoadWarningVal, insBranchIfNotWarning);

            il.InsertAfter(insBranchIfNotWarning, insLoadFormatString);
            il.InsertAfter(insLoadFormatString, insCallFilter);
            il.InsertAfter(insCallFilter, insBranchIfFalse);

            il.InsertAfter(insBranchIfFalse, insReturn);

            Console.WriteLine("[CS2AssetLoadOptimizer] Custom Log Filter injected successfully.");
        }
    }

    // 这部分是运行时被游戏调用的过滤逻辑
    // 必须是 public static，否则注入的 IL 无法访问
    public static class LogFilter
    {
        // 使用 StringComparison.OrdinalIgnoreCase 提高性能
        // 缓存这些关键词
        private static readonly string[] _keywords = new[]
        {
            "Collision detected with new Asset",
            "Duplicate prefab ID",
            "Unable to parse declaration", // 某些mod的错误
            "Unsupported CSS pseudo class selector encountered", // 某些mod的错误
            "Duplicate",  // 常见重复定义警告
            "Discord"
        };

        // 此时 message 可能为 null
        public static bool ShouldSkipLog(string message)
        {
            if (string.IsNullOrEmpty(message)) return false;

            // 遍历检查
            // 为了极致性能，不使用 Linq，使用原生循环
            for (int i = 0; i < _keywords.Length; i++)
            {
                if (message.IndexOf(_keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true; // 命中关键词，跳过日志
                }
            }

            return false; // 不跳过
        }
    }
}