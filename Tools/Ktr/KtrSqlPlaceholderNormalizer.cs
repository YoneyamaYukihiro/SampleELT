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
            IReadOnlyList<string>? defaultPlaceholderNames)
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
                warnings.Add(
                    $"DBInput '{stepName}': SQL 内の `?` のうち {unmatched} 件は前段 SetVariable のフィールド名で解決できず " +
                    ":{p<n>} 形式にフォールバックしました。手動で書き換えてください。");
            }
            return sb.ToString();
        }
    }
}
