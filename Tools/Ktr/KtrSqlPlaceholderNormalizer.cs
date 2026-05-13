using System.Collections.Generic;
using System.Text;

namespace SampleELT.Tools.Ktr
{
    /// <summary>
    /// KTR の <c>?</c> プレースホルダを SampleELT の <c>:{name}</c> 形式に置換する。
    /// 前段 SetVariable のフィールド名が判明していれば出現順に当てはめ、
    /// 不明な分は <c>:{p1}, :{p2}, ...</c> のフォールバックを使い警告を出す。
    /// </summary>
    internal static class KtrSqlPlaceholderNormalizer
    {
        public static string Normalize(
            string sql,
            List<string> warnings,
            string stepName,
            IReadOnlyList<string>? defaultPlaceholderNames,
            string? lookupStepName = null)
        {
            int count = 0;
            int unmatched = 0;
            var sb = new StringBuilder(sql.Length + 32);
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                if (c == '\'' && !inDoubleQuote) inSingleQuote = !inSingleQuote;
                else if (c == '"' && !inSingleQuote) inDoubleQuote = !inDoubleQuote;

                if (c == '?' && !inSingleQuote && !inDoubleQuote)
                {
                    count++;
                    string name;
                    if (defaultPlaceholderNames != null && count - 1 < defaultPlaceholderNames.Count)
                    {
                        name = defaultPlaceholderNames[count - 1];
                    }
                    else
                    {
                        name = "p" + count;
                        unmatched++;
                    }
                    sb.Append(":{").Append(name).Append('}');
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (count > 0 && unmatched > 0)
            {
                var hint = !string.IsNullOrEmpty(lookupStepName)
                    ? $"前段ステップ '{lookupStepName}' の出力フィールドを :{{p1}}, :{{p2}} に当てはめてください"
                    : "前段の SetVariable / DBInput が出力するフィールド名で :{p<n>} を置き換えてください";

                warnings.Add(
                    $"DBInput '{stepName}': SQL 内の `?` のうち {unmatched} 件を :{{p<n>}} 形式にフォールバックしました。" +
                    $"{hint}。");
            }
            return sb.ToString();
        }
    }
}
