using System.ComponentModel;
using System.Windows.Media;

namespace VerificarDeXMLNFCE
{
    // ═══════════════════════════════════════════════════════════════════
    //  Status da consulta SEFAZ
    // ═══════════════════════════════════════════════════════════════════
    public enum StatusConsulta
    {
        ComPagamento,
        SemPagamento,
        Erro,
        SemChave
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Resultado bruto da consulta SEFAZ
    // ═══════════════════════════════════════════════════════════════════
    public class ResultadoSefaz
    {
        public StatusConsulta Status      { get; set; } = StatusConsulta.Erro;
        public string DataPagamento       { get; set; } = "";
        public string Observacao          { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Informações extraídas do XML local
    // ═══════════════════════════════════════════════════════════════════
    public class NfceInfo
    {
        public string ChaveAcesso    { get; set; } = "";
        public string NumeroNF       { get; set; } = "";
        public string Emitente       { get; set; } = "";  // CNPJ ou Razão Social
        public string DataEmissao    { get; set; } = "";
        public string ValorTotal     { get; set; } = "";
        public string Fonte          { get; set; } = "";

        // Preenchidos após consulta SEFAZ
        public string DataPagamento  { get; set; } = "";
        public StatusConsulta StatusSefaz { get; set; } = StatusConsulta.Erro;
        public string Observacao     { get; set; } = "";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Item exibido no DataGrid (ViewModel)
    // ═══════════════════════════════════════════════════════════════════
    public class NotaFiscalItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int    Index          { get; set; }
        public string ChaveResumida  { get; set; } = "";
        public string ChaveCompleta  { get; set; } = "";
        public string Emitente       { get; set; } = "";
        public string DataEmissao    { get; set; } = "";
        public string DataPagamento  { get; set; } = "";
        public string ValorTotal     { get; set; } = "";
        public string Fonte          { get; set; } = "";
        public string Observacao     { get; set; } = "";
        public StatusConsulta Status { get; set; }

        public string StatusLabel => Status switch
        {
            StatusConsulta.ComPagamento => "✅  Autorizada",
            StatusConsulta.SemPagamento => "❌  Não autorizada",
            StatusConsulta.SemChave => "⚠️  Sem chave",
            _ => "⚠️  Não confirmada"
        };

        public Brush StatusBackground => Status switch
        {
            StatusConsulta.ComPagamento => new SolidColorBrush(Color.FromRgb(15, 42, 26)),
            StatusConsulta.SemPagamento => new SolidColorBrush(Color.FromRgb(42, 15, 15)),
            _                           => new SolidColorBrush(Color.FromRgb(26, 21, 0))
        };

        public Brush StatusForeground => Status switch
        {
            StatusConsulta.ComPagamento => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
            StatusConsulta.SemPagamento => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
            _                           => new SolidColorBrush(Color.FromRgb(234, 179, 8))
        };

        public Brush PagamentoForeground => Status == StatusConsulta.ComPagamento
            ? new SolidColorBrush(Color.FromRgb(34, 197, 94))
            : new SolidColorBrush(Color.FromRgb(100, 116, 139));

        public string PagamentoFontWeight => Status == StatusConsulta.ComPagamento ? "SemiBold" : "Normal";

        // ─── Fábricas ────────────────────────────────────────────────────
        public static NotaFiscalItem FromInfo(int idx, NfceInfo info)
        {
            string chaveResumida = info.ChaveAcesso.Length == 44
                ? $"{info.ChaveAcesso[..4]}…{info.ChaveAcesso[^6..]}"
                : (string.IsNullOrEmpty(info.NumeroNF) ? "(sem chave)" : $"NF {info.NumeroNF}");

            return new NotaFiscalItem
            {
                Index          = idx,
                ChaveResumida  = chaveResumida,
                ChaveCompleta  = info.ChaveAcesso,
                Emitente       = FormatarCnpj(info.Emitente),
                DataEmissao    = info.DataEmissao,
                DataPagamento  = string.IsNullOrEmpty(info.DataPagamento) ? "—" : info.DataPagamento,
                ValorTotal     = info.ValorTotal,
                Fonte          = info.Fonte,
                Observacao     = info.Observacao,
                Status         = info.StatusSefaz
            };
        }

        public static NotaFiscalItem Erro(int idx, string entrada, string fonte, string mensagem)
        {
            return new NotaFiscalItem
            {
                Index         = idx,
                ChaveResumida = entrada.Length > 20 ? $"{entrada[..4]}…" : entrada,
                ChaveCompleta = entrada,
                Emitente      = "—",
                DataEmissao   = "—",
                DataPagamento = "—",
                ValorTotal    = "—",
                Fonte         = fonte,
                Observacao    = mensagem,
                Status        = StatusConsulta.Erro
            };
        }

        private static string FormatarCnpj(string cnpj)
        {
            if (string.IsNullOrEmpty(cnpj)) return "—";
            cnpj = new string(cnpj.Where(char.IsDigit).ToArray());
            if (cnpj.Length == 14)
                return $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}";
            return cnpj;
        }
    }
}
