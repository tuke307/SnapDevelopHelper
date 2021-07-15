using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.IO;
using System.Linq;

namespace SnapDevelop.Helper
{
    public class PBtoDotNet
    {
        private OdbcConnection DbConnection;
        private const string Comment = "///";

        private List<string> SummaryXML = new List<string>()
            {
                "/// <summary>",
                 "/// {0}.",
                 "/// </summary>"
            };

        private static bool ImplementedClass = !settings.Default.BaseClass;
        private const string KeyAttribute = "[Key]";
        private const string Inheritdoc = "/// <inheritdoc />";
        private const string PropertyAttributeFirst = "[SqlColumn(";
        private const string PropertyAttributeLast = ")]";
        private OdbcCommand DbCommand;

        private OdbcDataReader DbReader;
        private List<Pbcatcol> Properties = new List<Pbcatcol>();

        private void OpenConn()
        {
            DbConnection = new OdbcConnection(settings.Default.ConnectionString);
            DbConnection.Open();
        }

        private void GetComments()
        {
            DbCommand = DbConnection.CreateCommand();
            string sql =
             @"SELECT
               pbc_cnam as 'Spalte',
               pbc_cmnt as 'Kommentar'
               FROM
               pbcatcol
               WHERE
               pbc_tnam = '{0}'";

            sql = string.Format(sql, settings.Default.Table);

            DbCommand.CommandText = sql;
            DbReader = DbCommand.ExecuteReader();

            while (DbReader.Read())
            {
                Pbcatcol pbcatcol = new Pbcatcol()
                {
                    Spalte = DbReader.GetString(0),
                    Kommentar = DbReader.SafeGetString(1)
                };

                pbcatcol.Kommentar.Replace("~r", "");
                pbcatcol.Kommentar.Replace("~n", "");
                pbcatcol.Kommentar.Replace("\r", "");
                pbcatcol.Kommentar.Replace("\n", "");

                Properties.Add(pbcatcol);
            }
        }

        public void RestructureProperties()
        {
            List<string> lines = File.ReadAllLines(settings.Default.FilePath).ToList();
            Pbcatcol property;
            int searchstartIndex = 0;
            bool commentAlreadyExist = false;

            // spaces entfernen
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i] = lines[i].TrimStart();
            }

            do
            {
                int matchIndex = lines.FindIndex(searchstartIndex, lines.Count() - searchstartIndex, x => x.Contains(PropertyAttributeFirst));

                // bei letzter property wird nichts mehr gefunden
                if (matchIndex == -1)
                    break;

                string propertyName = Utils.GetBetween(lines[matchIndex], PropertyAttributeFirst, PropertyAttributeLast);

                // da string in string: \"property\"
                propertyName = propertyName.Trim('\"');

                // start und ende ermitteln
                int startIndex = lines.LastIndexOf(string.Empty, matchIndex) + 1;
                int endIndex = lines.IndexOf(string.Empty, matchIndex);

                // gibt es ein kommentar?
                var match = Properties.Where(x => x.Spalte.Contains(propertyName));
                if (match.Any())
                    property = match.First();
                else
                    property = null;

                // wenn letzte property
                if (endIndex == -1)
                {
                    endIndex = lines.IndexOf("}", matchIndex);
                }

                List<string> propertyLines = lines.GetRange(startIndex, endIndex - startIndex);

                // wenn erste property
                if (propertyLines.Where(x => x.Contains("namespace")).Any())
                {
                    startIndex = lines.LastIndexOf("{", matchIndex) + 1;

                    propertyLines = lines.GetRange(startIndex, endIndex - startIndex);
                }

                if (ImplementedClass)
                {
                    int endSummary = propertyLines.IndexOf(SummaryXML[2]);

                    if (endSummary != -1)
                    {
                        propertyLines.RemoveRange(0, endSummary + 1);

                        propertyLines.Insert(0, Inheritdoc);
                    }
                }

                if (settings.Default.BaseClass)
                {
                    #region Kommentar

                    // wsummary tag schon vorhanden?
                    var matches = propertyLines.Where(stringToCheck => stringToCheck.Contains(Comment, StringComparison.OrdinalIgnoreCase));
                    commentAlreadyExist = matches.Any();

                    if (commentAlreadyExist)
                    {
                        if (settings.Default.DeleteCurrentComment)
                            propertyLines.RemoveRange(0, matches.Count());
                    }

                    // Kommentar einfügen
                    if (settings.Default.DeleteCurrentComment && !commentAlreadyExist)
                    {
                        List<string> summaryXMLeditable = new List<string>(SummaryXML);
                        summaryXMLeditable[1] = string.Format(summaryXMLeditable[1], property?.Kommentar);
                        propertyLines.InsertRange(0, summaryXMLeditable);
                    }

                    #endregion Kommentar

                    #region EigenschaftsName

                    // namenszeile
                    string propertyLine = propertyLines.Last();
                    // name zusammenbauen
                    string newpropertyName = char.ToUpper(settings.Default.Table[0]) + settings.Default.Table.Substring(1).ToLower() +
                                            "_" +
                                          char.ToUpper(propertyName[0]) + propertyName.Substring(1).ToLower();

                    #endregion EigenschaftsName

                    #region EigenschaftsDatentyp

                    // name ändern
                    // 0. => zugriffsmodifziere (public)
                    // 1. => datentyp
                    // 2. => name
                    var list = propertyLine.Split(' ').ToList();

                    switch (list[1])
                    {
                        case "Int16":
                            list[1] = "int";
                            break;

                        case "Int32":
                            list[1] = "int";
                            break;

                        case "String":
                            list[1] = "string";
                            break;

                        case "Decimal":
                            list[1] = "decimal";
                            break;

                        case "Double":
                            list[1] = "double";
                            break;

                        case "Byte":
                            list[1] = "byte";
                            break;
                    }

                    // nullable
                    if (list[1].Last() != '?')
                    {
                        if (settings.Default.PropertiesNullable && !propertyLines.Contains(KeyAttribute))
                            list[1] = list[1] + "?";

                        if (settings.Default.KeysNullable && propertyLines.Contains(KeyAttribute))
                            list[1] = list[1] + "?";
                    }

                    list[2] = newpropertyName;
                    propertyLine = string.Join(" ", list);

                    #endregion EigenschaftsDatentyp

                    // property line ersetzen
                    propertyLines.Remove(propertyLines.Last());
                    propertyLines.Add(propertyLine);
                }

                // Property inklusive Kommentar einfügen
                lines.RemoveRange(startIndex, endIndex - startIndex);
                lines.InsertRange(startIndex, propertyLines);

                // nächstes suchen
                searchstartIndex = matchIndex + 1;

                // kommentar neu hinzugefügt => um 3 erhöhen
                if (!commentAlreadyExist)
                    searchstartIndex += 3;
            } while (true && searchstartIndex < lines.Count());

            File.WriteAllLines(settings.Default.FilePath, lines);
        }

        public PBtoDotNet()
        {
            OpenConn();
            GetComments();
            RestructureProperties();
        }

        ~PBtoDotNet()
        {
            DbReader.Close();
            DbCommand.Dispose();
            DbConnection.Close();
        }
    }
}