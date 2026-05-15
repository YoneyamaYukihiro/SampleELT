using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BreezeFlow.Tools
{
    /// <summary>
    /// SELECT 文の出力カラム名を構文解析で抽出するベストエフォートのヘルパー。
    /// パラメータ (`?` / `:{name}`) が含まれていても解析できる。
    /// 完璧な SQL パーサではないため、複雑な式 (関数呼び出しに alias 無しなど) は取りこぼす。
    /// </summary>
    public static class SqlColumnExtractor
    {
        /// <summary>
        /// SQL 文から SELECT 句のカラム名一覧を抽出する。失敗時は空リストを返す。
        /// </summary>
        public static List<string> Extract(string sql)
        {
            if (string.IsNullOrWhiteSpace(sql)) return new();

            var cleaned = StripComments(sql);

            // 最初の SELECT ... FROM を抽出 (RegexOptions.Singleline で改行をまたぐ)
            var match = Regex.Match(cleaned,
                @"\bSELECT\b(.*?)\bFROM\b",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!match.Success) return new();

            var fieldsRaw = match.Groups[1].Value.Trim();
            // 修飾子 (DISTINCT / TOP N / ALL) を取り除く
            fieldsRaw = Regex.Replace(fieldsRaw,
                @"^\s*(DISTINCT|TOP\s+\d+|ALL)\s+",
                "", RegexOptions.IgnoreCase);

            var fields = SplitCommaIgnoringParens(fieldsRaw);
            var columns = new List<string>();
            foreach (var f in fields)
            {
                var name = ExtractFieldName(f.Trim());
                if (!string.IsNullOrEmpty(name)) columns.Add(name);
            }
            return columns;
        }

        /// <summary>-- 行コメント / /* */ ブロックコメントを除去。</summary>
        private static string StripComments(string sql)
        {
            // ブロックコメント
            sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
            // 行コメント
            sql = Regex.Replace(sql, @"--[^\r\n]*", " ");
            return sql;
        }

        /// <summary>括弧の中のカンマを区切りに使わないでトップレベルのカンマで分割。</summary>
        private static List<string> SplitCommaIgnoringParens(string s)
        {
            var result = new List<string>();
            var sb = new System.Text.StringBuilder();
            int depth = 0;
            bool inSingleQuote = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\'' && (i == 0 || s[i - 1] != '\\')) inSingleQuote = !inSingleQuote;

                if (!inSingleQuote)
                {
                    if (c == '(') depth++;
                    else if (c == ')') depth--;
                    else if (c == ',' && depth == 0)
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                        continue;
                    }
                }
                sb.Append(c);
            }
            if (sb.Length > 0) result.Add(sb.ToString());
            return result;
        }

        /// <summary>
        /// 1 つのフィールド式から表示名を取り出す。
        /// 優先順位: 1) AS alias、2) スペース区切りの末尾識別子 (alias)、3) table.col → col、4) ベアな識別子
        /// </summary>
        private static string ExtractFieldName(string field)
        {
            // AS alias
            var asMatch = Regex.Match(field, @"\bAS\s+(""?)([a-zA-Z_][\w$]*)\1\s*$",
                RegexOptions.IgnoreCase);
            if (asMatch.Success) return asMatch.Groups[2].Value;

            // 末尾が `)` (関数式に alias 無し) なら抽出不能
            if (field.TrimEnd().EndsWith(")")) return "";

            // 末尾識別子: "table.col" 全体や "col alias" の alias を取り出す
            var trailing = Regex.Match(field,
                @"(?:^|[\s.)])([a-zA-Z_][\w$]*)\s*$");
            if (!trailing.Success) return "";
            return trailing.Groups[1].Value;
        }
    }
}
