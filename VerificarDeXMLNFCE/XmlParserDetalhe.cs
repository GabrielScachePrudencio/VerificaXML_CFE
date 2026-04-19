using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace VerificarDeXMLNFCE
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  Modelo detalhado de uma nota (usado só na ConsultaNotaWindow)
    // ═══════════════════════════════════════════════════════════════════════════
    public class NfceDetalhe
    {
        // Identificação
        public string ChaveAcesso   { get; set; } = "";
        public string NumeroNF      { get; set; } = "";
        public string Serie         { get; set; } = "";
        public string Modelo        { get; set; } = "";
        public string DataEmissao   { get; set; } = "";
        public string DataSaida     { get; set; } = "";
        public string NatOp         { get; set; } = "";
        public string Fonte         { get; set; } = "";

        // Emitente
        public string EmitNome  { get; set; } = "";
        public string EmitCnpj  { get; set; } = "";
        public string EmitIE    { get; set; } = "";
        public string EmitLogr  { get; set; } = "";
        public string EmitNum   { get; set; } = "";
        public string EmitBairro{ get; set; } = "";
        public string EmitMun   { get; set; } = "";
        public string EmitUF    { get; set; } = "";
        public string EmitCep   { get; set; } = "";
        public string EmitFone  { get; set; } = "";

        // Destinatário
        public string DestNome    { get; set; } = "";
        public string DestCpfCnpj { get; set; } = "";
        public string DestIE      { get; set; } = "";
        public string DestLogr    { get; set; } = "";
        public string DestNum     { get; set; } = "";
        public string DestBairro  { get; set; } = "";
        public string DestMun     { get; set; } = "";
        public string DestUF      { get; set; } = "";
        public string DestCep     { get; set; } = "";

        // Totais
        public string ValorTotal { get; set; } = "";
        public string ValorProd  { get; set; } = "";
        public string Desconto   { get; set; } = "";
        public string Frete      { get; set; } = "";
        public string Seguro     { get; set; } = "";
        public string ValIcms    { get; set; } = "";

        // Pagamento
        public string FormaPagamento { get; set; } = "";
        public string Troco          { get; set; } = "";
        public string DataPagamento  { get; set; } = "";
        public StatusConsulta StatusSefaz { get; set; } = StatusConsulta.Erro;
        public string Observacao     { get; set; } = "";

        // Protocolo
        public string Protocolo      { get; set; } = "";
        public string DtAutorizacao  { get; set; } = "";
        public string DigestValue    { get; set; } = "";

        // Info adicional
        public string InfFisco   { get; set; } = "";
        public string InfContrib { get; set; } = "";

        // Produtos
        public List<ProdutoItem> Produtos { get; set; } = new();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Linha de produto para o DataGrid
    // ═══════════════════════════════════════════════════════════════════════════
    public class ProdutoItem
    {
        public string Item      { get; set; } = "";
        public string Codigo    { get; set; } = "";
        public string Descricao { get; set; } = "";
        public string Ncm       { get; set; } = "";
        public string Cfop      { get; set; } = "";
        public string Qtd       { get; set; } = "";
        public string Unidade   { get; set; } = "";
        public string ValUnit   { get; set; } = "";
        public string ValTotal  { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  Parser detalhado
    // ═══════════════════════════════════════════════════════════════════════════
    public static class XmlParserDetalhe
    {
        private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
        private static readonly CultureInfo PtBR = new("pt-BR");
        private static readonly CultureInfo InvCul = CultureInfo.InvariantCulture;

        public static NfceDetalhe ParseDetalhado(string caminhoXml)
        {
            var d = new NfceDetalhe();
            var doc = XDocument.Load(caminhoXml);

            // Aceita nfeProc ou NFe diretamente
            XElement? infNFe = doc.Descendants(Ns + "infNFe").FirstOrDefault()
                            ?? doc.Descendants("infNFe").FirstOrDefault();
            if (infNFe == null) throw new Exception("Elemento <infNFe> não encontrado.");

            // ── Chave ──────────────────────────────────────────────────────────
            string id = infNFe.Attribute("Id")?.Value ?? "";
            if (id.StartsWith("NFe", StringComparison.OrdinalIgnoreCase)) id = id[3..];
            d.ChaveAcesso = id;

            // ── ide ─────────────────────────────────────────────────────────────
            var ide = Get(infNFe, "ide");
            if (ide != null)
            {
                d.NumeroNF = Txt(ide, "nNF");
                d.Serie = Txt(ide, "serie");
                d.Modelo = Txt(ide, "mod");
                d.NatOp = Txt(ide, "natOp");
                d.DataEmissao = ParseData(Txt(ide, "dhEmi"));
                d.DataSaida = ParseData(Txt(ide, "dhSaiEnt").Length > 0
                                    ? Txt(ide, "dhSaiEnt")
                                    : Txt(ide, "dSaiEnt"));
            }

            // ── emit ────────────────────────────────────────────────────────────
            var emit = Get(infNFe, "emit");
            if (emit != null)
            {
                d.EmitNome = Txt(emit, "xNome") is { Length: > 0 } n ? n : Txt(emit, "xFant");
                d.EmitCnpj = Txt(emit, "CNPJ") is { Length: > 0 } c ? c : Txt(emit, "CPF");
                d.EmitIE = Txt(emit, "IE");
                d.EmitFone = Txt(emit, "fone");
                var ea = Get(emit, "enderEmit");
                if (ea != null)
                {
                    d.EmitLogr = Txt(ea, "xLgr");
                    d.EmitNum = Txt(ea, "nro");
                    d.EmitBairro = Txt(ea, "xBairro");
                    d.EmitMun = Txt(ea, "xMun");
                    d.EmitUF = Txt(ea, "UF");
                    d.EmitCep = FormatarCep(Txt(ea, "CEP"));
                }
            }

            // ── dest ────────────────────────────────────────────────────────────
            var dest = Get(infNFe, "dest");
            if (dest != null)
            {
                d.DestNome = Txt(dest, "xNome");
                d.DestCpfCnpj = Txt(dest, "CNPJ") is { Length: > 0 } c2 ? c2 : Txt(dest, "CPF");
                d.DestIE = Txt(dest, "IE");
                var da = Get(dest, "enderDest");
                if (da != null)
                {
                    d.DestLogr = Txt(da, "xLgr");
                    d.DestNum = Txt(da, "nro");
                    d.DestBairro = Txt(da, "xBairro");
                    d.DestMun = Txt(da, "xMun");
                    d.DestUF = Txt(da, "UF");
                    d.DestCep = FormatarCep(Txt(da, "CEP"));
                }
            }

            // ── produtos ────────────────────────────────────────────────────────
            int itemNum = 1;
            foreach (var det in infNFe.Elements(Ns + "det").Concat(infNFe.Elements("det")))
            {
                var prod = Get(det, "prod");
                if (prod == null) continue;

                decimal qtd = Dec(Txt(prod, "qCom"));
                decimal vu = Dec(Txt(prod, "vUnCom"));
                decimal vt = Dec(Txt(prod, "vProd"));

                d.Produtos.Add(new ProdutoItem
                {
                    Item = (itemNum++).ToString(),
                    Codigo = Txt(prod, "cProd"),
                    Descricao = Txt(prod, "xProd"),
                    Ncm = Txt(prod, "NCM"),
                    Cfop = Txt(prod, "CFOP"),
                    Qtd = qtd.ToString("N3", PtBR),
                    Unidade = Txt(prod, "uCom"),
                    ValUnit = vu.ToString("C2", PtBR),
                    ValTotal = vt.ToString("C2", PtBR)
                });
            }

            // ── ICMSTot ─────────────────────────────────────────────────────────
            var tot = infNFe.Descendants(Ns + "ICMSTot").FirstOrDefault()
                   ?? infNFe.Descendants("ICMSTot").FirstOrDefault();
            if (tot != null)
            {
                d.ValorTotal = Dec(Txt(tot, "vNF")).ToString("C2", PtBR);
                d.ValorProd = Dec(Txt(tot, "vProd")).ToString("C2", PtBR);
                d.Desconto = Dec(Txt(tot, "vDesc")).ToString("C2", PtBR);
                d.Frete = Dec(Txt(tot, "vFrete")).ToString("C2", PtBR);
                d.Seguro = Dec(Txt(tot, "vSeg")).ToString("C2", PtBR);
                d.ValIcms = Dec(Txt(tot, "vICMS")).ToString("C2", PtBR);
            }

            // ── pagamento ────────────────────────────────────────────────────────
            var pgtos = infNFe.Descendants(Ns + "detPag").Concat(infNFe.Descendants("detPag")).ToList();
            if (pgtos.Count == 0)
                pgtos = infNFe.Descendants(Ns + "pag").Concat(infNFe.Descendants("pag")).ToList();

            var formas = new List<string>();
            foreach (var pg in pgtos)
            {
                string tPag = Txt(pg, "tPag");
                string vPag = Dec(Txt(pg, "vPag")).ToString("C2", PtBR);
                if (!string.IsNullOrEmpty(tPag))
                    formas.Add($"{DescricaoFormaPagamento(tPag)} ({vPag})");
            }
            d.FormaPagamento = formas.Count > 0 ? string.Join(" + ", formas) : "—";

            var pagNode = infNFe.Descendants(Ns + "pag").FirstOrDefault()
                       ?? infNFe.Descendants("pag").FirstOrDefault();
            if (pagNode != null)
                d.Troco = Dec(Txt(pagNode, "vTroco")).ToString("C2", PtBR);

            // ── informações adicionais ───────────────────────────────────────────
            var infAdic = Get(infNFe, "infAdic");
            if (infAdic != null)
            {
                d.InfFisco = Txt(infAdic, "infAdFisco");
                d.InfContrib = Txt(infAdic, "infCpl");
            }

            // ── protocolo — busca direta no documento todo para evitar problema de namespace ──
            var infProt = doc.Descendants(Ns + "infProt").FirstOrDefault()
                       ?? doc.Descendants("infProt").FirstOrDefault();

            if (infProt == null)
            {
                d.StatusSefaz = StatusConsulta.Erro;
                d.Observacao = "⚠ Nota não confirmada pelo SEFAZ";
            }
            if (infProt != null)
            {
                string cStat = infProt.Descendants(Ns + "cStat").FirstOrDefault()?.Value
                            ?? infProt.Descendants("cStat").FirstOrDefault()?.Value
                            ?? infProt.Element(Ns + "cStat")?.Value
                            ?? infProt.Element("cStat")?.Value
                            ?? "";

                d.StatusSefaz = cStat switch
                {
                    "100" => StatusConsulta.ComPagamento,
                    "101" or "102" or "110" => StatusConsulta.SemPagamento,
                    _ => StatusConsulta.Erro
                };

                d.Observacao = cStat switch
                {
                    "100" => "✔ EMITIDA (AUTORIZADA)",
                    "101" => "❌ CANCELADA",
                    "102" => "❌ INUTILIZADA",
                    "110" => "⚠ DENEGADA",
                    _ => $"❓ STATUS DESCONHECIDO ({cStat})"
                };

                d.Protocolo = infProt.Descendants(Ns + "nProt").FirstOrDefault()?.Value
                           ?? infProt.Element(Ns + "nProt")?.Value
                           ?? infProt.Element("nProt")?.Value
                           ?? "";

                d.DtAutorizacao = ParseData(
                           infProt.Descendants(Ns + "dhRecbto").FirstOrDefault()?.Value
                        ?? infProt.Element(Ns + "dhRecbto")?.Value
                        ?? infProt.Element("dhRecbto")?.Value
                        ?? "");
            }
            else
            {
                // Sem protNFe = XML gerado mas não retornou autorização do SEFAZ
                d.StatusSefaz = StatusConsulta.Erro;
                d.Observacao = "⚠ Nota gerada, mas sem confirmação do SEFAZ";
            }

            // ── DigestValue ──────────────────────────────────────────────────────
            var sig = doc.Descendants(XName.Get("DigestValue", "http://www.w3.org/2000/09/xmldsig#")).FirstOrDefault();
            d.DigestValue = sig?.Value ?? "";

            return d;
        }
        // ─── Helpers ──────────────────────────────────────────────────────────
        private static XElement? Get(XElement parent, string name)
            => parent.Element(Ns + name) ?? parent.Element(name);

        private static string Txt(XElement? parent, string name)
            => parent?.Element(Ns + name)?.Value ?? parent?.Element(name)?.Value ?? "";

        private static decimal Dec(string s)
            => decimal.TryParse(s, NumberStyles.Any, InvCul, out var v) ? v : 0m;

        private static string ParseData(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "—";
            return DateTime.TryParse(raw, out var dt)
                ? dt.ToString("dd/MM/yyyy HH:mm")
                : raw;
        }

        private static string FormatarCep(string cep)
        {
            cep = new string(cep.Where(char.IsDigit).ToArray());
            return cep.Length == 8 ? $"{cep[..5]}-{cep[5..]}" : cep;
        }

        private static string DescricaoFormaPagamento(string tPag) => tPag switch
        {
            "01" => "Dinheiro",
            "02" => "Cheque",
            "03" => "Cartão de Crédito",
            "04" => "Cartão de Débito",
            "05" => "Crédito Loja",
            "10" => "Vale Alimentação",
            "11" => "Vale Refeição",
            "12" => "Vale Presente",
            "13" => "Vale Combustível",
            "14" => "Duplicata Mercantil",
            "15" => "Boleto Bancário",
            "16" => "Depósito Bancário",
            "17" => "PIX",
            "18" => "Transferência Bancária",
            "19" => "Programa Fidelidade",
            "90" => "Sem Pagamento",
            "99" => "Outros",
            _    => $"Forma {tPag}"
        };
    }
}
