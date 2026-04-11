using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace VerificarDeXMLNFCE
{
    /// <summary>
    /// Extrai as informações relevantes de um arquivo XML de NFC-e ou NF-e.
    /// Suporta os namespaces padrão: http://www.portalfiscal.inf.br/nfe
    /// </summary>
    public static class XmlParser
    {
        private static readonly XNamespace NsNFe  = "http://www.portalfiscal.inf.br/nfe";

        public static NfceInfo ParseXml(string caminhoXml)
        {
            var info = new NfceInfo { Fonte = "XML Local" };

            try
            {
                var doc = XDocument.Load(caminhoXml);
                var root = doc.Root;

                // Tenta encontrar o nó infNFe — pode estar dentro de nfeProc ou direto
                XElement? infNFe = root?.Descendants(NsNFe + "infNFe").FirstOrDefault()
                               ?? root?.Descendants("infNFe").FirstOrDefault();

                if (infNFe == null)
                    throw new Exception("Elemento <infNFe> não encontrado no XML.");

                // ── Chave de acesso (atributo Id, sem prefixo "NFe") ──────────────
                string id = infNFe.Attribute("Id")?.Value ?? "";
                if (id.StartsWith("NFe", StringComparison.OrdinalIgnoreCase))
                    id = id[3..];
                info.ChaveAcesso = id;

                // ── Bloco ide ─────────────────────────────────────────────────────
                var ide = infNFe.Element(NsNFe + "ide") ?? infNFe.Element("ide");
                if (ide != null)
                {
                    info.NumeroNF = GetText(ide, "nNF");
                    string dhEmi  = GetText(ide, "dhEmi");
                    if (!string.IsNullOrEmpty(dhEmi) && DateTime.TryParse(dhEmi, out var dt))
                        info.DataEmissao = dt.ToString("dd/MM/yyyy HH:mm");
                }

                // ── Emitente ──────────────────────────────────────────────────────
                var emit = infNFe.Element(NsNFe + "emit") ?? infNFe.Element("emit");
                if (emit != null)
                {
                    info.Emitente = GetText(emit, "CNPJ");
                    if (string.IsNullOrEmpty(info.Emitente))
                        info.Emitente = GetText(emit, "CPF");
                }

                // ── Totais ────────────────────────────────────────────────────────
                var total = infNFe.Descendants(NsNFe + "ICMSTot").FirstOrDefault()
                         ?? infNFe.Descendants("ICMSTot").FirstOrDefault();
                if (total != null)
                {
                    string vNF = GetText(total, "vNF");
                    if (decimal.TryParse(vNF, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out decimal val))
                        info.ValorTotal = val.ToString("C2", new System.Globalization.CultureInfo("pt-BR"));
                }

                // ── Se a chave ainda estiver vazia, tenta extrair do nome do arquivo ──
                if (string.IsNullOrEmpty(info.ChaveAcesso))
                {
                    string nome = Path.GetFileNameWithoutExtension(caminhoXml);
                    if (nome.Length == 44 && nome.All(char.IsDigit))
                        info.ChaveAcesso = nome;
                }
            }
            catch (Exception ex)
            {
                info.Observacao    = $"Erro ao ler XML: {ex.Message}";
                info.StatusSefaz   = StatusConsulta.Erro;
            }

            return info;
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────
        private static string GetText(XElement parent, string localName)
        {
            return parent.Element(NsNFe + localName)?.Value
                ?? parent.Element(localName)?.Value
                ?? "";
        }
    }
}
