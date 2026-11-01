using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Screenlocker.Services
{
    /// <summary>
    /// 模板渲染：把 {占位符} 换成实际值，并按行切分。
    /// 锁屏"详细状态"最多三行，所以模板最多用三行。
    /// </summary>
    public static class TemplateEngine
    {
        public const int MaxLines = 3;

        private static readonly Regex TokenPattern = new Regex(@"\{([A-Za-z0-9_]+)\}", RegexOptions.Compiled);

        /// <summary>按换行切分模板，去掉首尾空白与空行。</summary>
        public static List<string> SplitLines(string template)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(template))
            {
                return lines;
            }

            foreach (var raw in template.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        /// <summary>替换一行里的所有占位符；认不出来的占位符原样保留，方便用户发现自己写错。</summary>
        public static string RenderLine(string line, IReadOnlyDictionary<string, string> values)
        {
            if (string.IsNullOrEmpty(line) || values == null)
            {
                return line ?? string.Empty;
            }

            return TokenPattern.Replace(line, match =>
            {
                var key = match.Groups[1].Value;
                return values.TryGetValue(key, out var value) ? value : match.Value;
            });
        }

        /// <summary>渲染整段模板；行数超过三行时，多余的行并进第三行。</summary>
        public static List<string> RenderLines(string template, IReadOnlyDictionary<string, string> values)
        {
            var source = SplitLines(template);
            var result = new List<string>();

            for (var i = 0; i < source.Count && i < MaxLines; i++)
            {
                result.Add(RenderLine(source[i], values));
            }

            if (source.Count > MaxLines)
            {
                var overflow = new StringBuilder();
                for (var i = MaxLines; i < source.Count; i++)
                {
                    if (overflow.Length > 0)
                    {
                        overflow.Append(' ');
                    }

                    overflow.Append(RenderLine(source[i], values));
                }

                if (result.Count == MaxLines)
                {
                    result[MaxLines - 1] = (result[MaxLines - 1] + " " + overflow).Trim();
                }
                else
                {
                    result.Add(overflow.ToString());
                }
            }

            return result;
        }
    }
}
