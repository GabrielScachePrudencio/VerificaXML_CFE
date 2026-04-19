using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace VerificarDeXMLNFCE
{
    public partial class ConsultaNotaWindow : Window
    {
        private string _chaveFinal = "";
        private NfceDetalhe? _detalheAtual;

        public ConsultaNotaWindow()
        {
            InitializeComponent();
            txtChaveConsulta.TextChanged += (_, _) => AtualizarPlaceholder();
            Loaded += (_, _) => txtChaveConsulta.Focus();
        }

        /// <summary>
        /// Abre a janela já com uma chave preenchida e inicia a consulta automaticamente.
        /// Chamado quando o usuário dá double-click em uma linha do DataGrid principal.
        /// </summary>
        public void AbrirComChave(string chave)
        {
            txtChaveConsulta.Text = chave;
            AtualizarPlaceholder();
            _ = ExecutarConsulta();
        }

        // ─── Placeholder ──────────────────────────────────────────────────────
        private void AtualizarPlaceholder()
            => txtPlaceholder.Visibility = string.IsNullOrEmpty(txtChaveConsulta.Text)
                ? Visibility.Visible : Visibility.Collapsed;

        // ─── Enter no campo ───────────────────────────────────────────────────
        private void TxtChave_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) _ = ExecutarConsulta();
        }

        // ─── Botão: Colar XML da área de transferência ────────────────────────
        private void BtnColarXml_Click(object sender, RoutedEventArgs e)
        {
            string clipText = Clipboard.GetText().Trim();
            if (string.IsNullOrEmpty(clipText)) { MsgAviso("Área de transferência vazia."); return; }

            if (clipText.Contains("<NFe") || clipText.Contains("<nfeProc") || clipText.Contains("<infNFe"))
            {
                ProcessarXmlTexto(clipText, "XML colado");
            }
            else if (clipText.Replace(" ", "").Length == 44 && clipText.Replace(" ", "").All(char.IsDigit))
            {
                txtChaveConsulta.Text = clipText.Replace(" ", "");
                _ = ExecutarConsulta();
            }
            else
            {
                MsgAviso("O conteúdo colado não é um XML de NF-e/NFC-e nem uma chave de acesso válida.");
            }
        }

        // ─── Botão: Abrir arquivo XML ─────────────────────────────────────────
        private void BtnAbrirXml_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Selecionar arquivo XML de NF-e / NFC-e",
                Filter = "Arquivos XML|*.xml|Todos os arquivos|*.*"
            };
            if (dlg.ShowDialog() != true) return;

            string conteudo = File.ReadAllText(dlg.FileName, Encoding.UTF8);
            ProcessarXmlTexto(conteudo, Path.GetFileName(dlg.FileName));
        }

        // ─── Botão principal: Consultar ───────────────────────────────────────
        private void BtnConsultarNota_Click(object sender, RoutedEventArgs e)
            => _ = ExecutarConsulta();

        // ─── Processar XML (texto bruto) ──────────────────────────────────────
        private void ProcessarXmlTexto(string xmlTexto, string fonte)
        {
            SetStatus("Processando XML…", "#3B82F6");
            MostrarLoading("Lendo arquivo XML…");

            try
            {
                // Salva temporariamente para o XmlParser
                string tmpPath = Path.Combine(Path.GetTempPath(), $"nfce_tmp_{Guid.NewGuid()}.xml");
                File.WriteAllText(tmpPath, xmlTexto, Encoding.UTF8);

                var info    = XmlParser.ParseXml(tmpPath);
                var detalhe = XmlParserDetalhe.ParseDetalhado(tmpPath);
                File.Delete(tmpPath);

                detalhe.Fonte = fonte;
                if (!string.IsNullOrEmpty(info.ChaveAcesso))
                {
                    txtChaveConsulta.Text = info.ChaveAcesso;
                    AtualizarPlaceholder();
                }

                _ = ConsultarEExibir(detalhe);
            }
            catch (Exception ex)
            {
                MostrarVazio();
                MsgAviso($"Erro ao ler o XML:\n{ex.Message}");
            }
        }

        // ─── Fluxo principal de consulta ──────────────────────────────────────
        private async Task ExecutarConsulta()
        {
            string entrada = txtChaveConsulta.Text.Trim().Replace(" ", "");
            if (string.IsNullOrEmpty(entrada)) { MsgAviso("Informe a chave de acesso."); return; }

            if (entrada.Length != 44 || !entrada.All(char.IsDigit))
            {
                MsgAviso("A chave de acesso deve ter exatamente 44 dígitos numéricos.");
                return;
            }

            MostrarLoading("Consultando portal SEFAZ…");
            SetStatus("Consultando…", "#3B82F6");
            btnConsultarNota.IsEnabled = false;

            try
            {
                var detalhe = new NfceDetalhe
                {
                    ChaveAcesso = entrada,
                    Fonte = "Chave manual"
                };
                await ConsultarEExibir(detalhe);
            }
            finally
            {
                btnConsultarNota.IsEnabled = true;
            }
        }

        private async Task ConsultarEExibir(NfceDetalhe detalhe)
        {
            if (!string.IsNullOrEmpty(detalhe.ChaveAcesso))
            {
                SetStatus("Consultando SEFAZ…", "#EAB308");
                var resultado = await SefazConsulta.ConsultarAsync(detalhe.ChaveAcesso, CancellationToken.None);
                // Só pega a data de pagamento, NUNCA altera status
                detalhe.DataPagamento = resultado.DataPagamento;
            }
            _detalheAtual = detalhe;
            _chaveFinal = detalhe.ChaveAcesso;
            ExibirDetalhe(detalhe);
            SetStatus($"Nota consultada — {detalhe.Fonte}", "#22C55E");
            // Se ainda não tem status definido mas tem chave válida = considera emitida
            if (detalhe.StatusSefaz == StatusConsulta.Erro &&
                !string.IsNullOrEmpty(detalhe.ChaveAcesso) &&
                detalhe.ChaveAcesso.Length == 44)
            {
                detalhe.StatusSefaz = StatusConsulta.ComPagamento;
                detalhe.Observacao = "✔ EMITIDA";
            }

            _detalheAtual = detalhe;
            _chaveFinal   = detalhe.ChaveAcesso;
            ExibirDetalhe(detalhe);
            SetStatus($"Nota consultada com sucesso — {detalhe.Fonte}", "#22C55E");
        }

        // ─── Exibir resultado na tela ─────────────────────────────────────────
        private void ExibirDetalhe(NfceDetalhe d)
        {
            MostrarResultado();

            // Status badge
            switch (d.StatusSefaz)
            {
                case StatusConsulta.ComPagamento:
                    badgeStatus.Background = new SolidColorBrush(Color.FromRgb(15, 42, 26));
                    lblStatusNota.Text = "✅  EMITIDA / AUTORIZADA";  // ← muda aqui
                    lblStatusNota.Foreground = new SolidColorBrush(Color.FromRgb(34, 197, 94));
                    break;
                case StatusConsulta.SemPagamento:
                    badgeStatus.Background = new SolidColorBrush(Color.FromRgb(42, 15, 15));
                    lblStatusNota.Text = "❌  NÃO EMITIDA / CANCELADA";  // ← muda aqui
                    lblStatusNota.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                    break;
                default:
                    badgeStatus.Background = new SolidColorBrush(Color.FromRgb(26, 21, 0));
                    lblStatusNota.Text     = "⚠️  " + (d.Observacao.Length > 60 ? d.Observacao[..60] + "…" : d.Observacao);
                    lblStatusNota.Foreground = new SolidColorBrush(Color.FromRgb(234, 179, 8));
                    break;
            }

            // Chave
            lblChaveCompleta.Text = FormatarChave(d.ChaveAcesso);

            // Identificação
            lblNumNF.Text      = string.IsNullOrEmpty(d.NumeroNF) ? "—" : d.NumeroNF.TrimStart('0');
            lblSerie.Text      = string.IsNullOrEmpty(d.Serie) ? "—" : d.Serie;
            lblModelo.Text     = d.Modelo == "65" ? "65 — NFC-e (Consumidor)" : d.Modelo == "55" ? "55 — NF-e" : (d.Modelo + " — Desconhecido");
            lblDataEmissao.Text = d.DataEmissao;
            lblDataSaida.Text  = string.IsNullOrEmpty(d.DataSaida) ? "—" : d.DataSaida;
            lblNatOp.Text      = string.IsNullOrEmpty(d.NatOp) ? "—" : d.NatOp;

            // Emitente
            lblEmitNome.Text  = d.EmitNome;
            lblEmitCnpj.Text  = FormatarCnpj(d.EmitCnpj);
            lblEmitIE.Text    = string.IsNullOrEmpty(d.EmitIE) ? "—" : d.EmitIE;
            lblEmitEnd.Text   = MontarEndereco(d.EmitLogr, d.EmitNum, d.EmitBairro, d.EmitCep);
            lblEmitMunUF.Text = $"{d.EmitMun} / {d.EmitUF}";
            lblEmitFone.Text  = FormatarFone(d.EmitFone);

            // Destinatário
            lblDestNome.Text   = string.IsNullOrEmpty(d.DestNome) ? "Consumidor Final" : d.DestNome;
            lblDestCpfCnpj.Text = string.IsNullOrEmpty(d.DestCpfCnpj) ? "—" : FormatarDocumento(d.DestCpfCnpj);
            lblDestIE.Text     = string.IsNullOrEmpty(d.DestIE) ? "—" : d.DestIE;
            lblDestEnd.Text    = MontarEndereco(d.DestLogr, d.DestNum, d.DestBairro, d.DestCep);
            lblDestMunUF.Text  = $"{d.DestMun} / {d.DestUF}";

            // Valores
            lblValorTotal.Text = d.ValorTotal;
            lblValorProd.Text  = d.ValorProd;
            lblDesconto.Text   = string.IsNullOrEmpty(d.Desconto) || d.Desconto == "R$ 0,00" ? "—" : "- " + d.Desconto;
            lblFrete.Text      = d.Frete;
            lblIcms.Text       = d.ValIcms;

            // Pagamento
            lblFormaPgto.Text     = d.FormaPagamento;
            lblDataPgtoSefaz.Text = string.IsNullOrEmpty(d.DataPagamento) ? "— (não encontrada)" : d.DataPagamento;
            lblDataPgtoSefaz.Foreground = string.IsNullOrEmpty(d.DataPagamento)
                ? new SolidColorBrush(Color.FromRgb(100, 116, 139))
                : new SolidColorBrush(Color.FromRgb(34, 197, 94));
            lblTroco.Text = string.IsNullOrEmpty(d.Troco) || d.Troco == "R$ 0,00" ? "—" : d.Troco;

            // Produtos
            dgProdutos.ItemsSource = d.Produtos;
            lblQtdItens.Text       = $"{d.Produtos.Count} item(ns)";

            // Info adicional
            lblInfFisco.Text   = string.IsNullOrEmpty(d.InfFisco) ? "—" : d.InfFisco;
            lblInfContrib.Text = string.IsNullOrEmpty(d.InfContrib) ? "—" : d.InfContrib;

            // Protocolo
            lblProtocolo.Text    = string.IsNullOrEmpty(d.Protocolo) ? "—" : d.Protocolo;
            lblDtAutorizacao.Text = string.IsNullOrEmpty(d.DtAutorizacao) ? "—" : d.DtAutorizacao;
            lblDigest.Text       = string.IsNullOrEmpty(d.DigestValue) ? "—" : d.DigestValue;
        }

        // ─── Ações dos botões ─────────────────────────────────────────────────
        private void CopiarChave_Click(object sender, MouseButtonEventArgs e) => CopiarChave();
        private void CopiarChaveBotao_Click(object sender, RoutedEventArgs e) => CopiarChave();
        private void CopiarChave()
        {
            if (!string.IsNullOrEmpty(_chaveFinal))
            {
                Clipboard.SetText(_chaveFinal);
                SetStatus("Chave de acesso copiada para a área de transferência!", "#22C55E");
            }
        }

        private void BtnAbrirSefaz_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_chaveFinal)) return;
            string cuf = _chaveFinal[..2];
            string url = SefazUrlHelper.ObterUrlConsulta(cuf, _chaveFinal);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }

        private void BtnExportarResumo_Click(object sender, RoutedEventArgs e)
        {
            if (_detalheAtual == null) return;
            var d = _detalheAtual;

            var dlg = new SaveFileDialog
            {
                Title    = "Exportar resumo da nota",
                Filter   = "Texto (*.txt)|*.txt|CSV (*.csv)|*.csv",
                FileName = $"NF_{d.NumeroNF}_{DateTime.Now:yyyyMMdd}"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("══════════════════════════════════════════");
            sb.AppendLine($"  RESUMO DA NOTA FISCAL  —  {DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine("══════════════════════════════════════════");
            sb.AppendLine($"Chave de Acesso : {d.ChaveAcesso}");
            sb.AppendLine($"Número NF       : {d.NumeroNF}  |  Série: {d.Serie}  |  Modelo: {d.Modelo}");
            sb.AppendLine($"Data Emissão    : {d.DataEmissao}");
            sb.AppendLine($"Status SEFAZ    : {d.StatusSefaz}");
            sb.AppendLine($"Data Pagamento  : {(string.IsNullOrEmpty(d.DataPagamento) ? "Não informada" : d.DataPagamento)}");
            sb.AppendLine($"Protocolo       : {d.Protocolo}");
            sb.AppendLine();
            sb.AppendLine($"EMITENTE: {d.EmitNome}  CNPJ: {d.EmitCnpj}");
            sb.AppendLine($"DESTINATÁRIO: {d.DestNome}  Doc: {d.DestCpfCnpj}");
            sb.AppendLine();
            sb.AppendLine($"Valor Produtos  : {d.ValorProd}");
            sb.AppendLine($"Desconto        : {d.Desconto}");
            sb.AppendLine($"VALOR TOTAL     : {d.ValorTotal}");
            sb.AppendLine($"Forma Pagamento : {d.FormaPagamento}");
            sb.AppendLine();
            sb.AppendLine("ITENS:");
            foreach (var p in d.Produtos)
                sb.AppendLine($"  {p.Item,3}. [{p.Codigo}] {p.Descricao,-40} {p.Qtd,8} {p.Unidade,-4}  {p.ValUnit,10}  {p.ValTotal,10}");

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            SetStatus($"Resumo exportado: {dlg.FileName}", "#22C55E");
        }

        // ─── Visibilidade dos painéis ─────────────────────────────────────────
        private void MostrarLoading(string msg)
        {
            painelVazio.Visibility      = Visibility.Collapsed;
            painelLoading.Visibility    = Visibility.Visible;
            painelResultado.Visibility  = Visibility.Collapsed;
            lblLoading.Text             = msg;
        }
        private void MostrarResultado()
        {
            painelVazio.Visibility      = Visibility.Collapsed;
            painelLoading.Visibility    = Visibility.Collapsed;
            painelResultado.Visibility  = Visibility.Visible;
        }
        private void MostrarVazio()
        {
            painelVazio.Visibility      = Visibility.Visible;
            painelLoading.Visibility    = Visibility.Collapsed;
            painelResultado.Visibility  = Visibility.Collapsed;
        }

        // ─── Helpers visuais ──────────────────────────────────────────────────
        private void SetStatus(string msg, string cor)
        {
            lblStatus.Text = msg;
            statusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(cor));
        }
        private static void MsgAviso(string msg)
            => MessageBox.Show(msg, "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);

        // ─── Formatadores ─────────────────────────────────────────────────────
        private static string FormatarChave(string c)
        {
            if (string.IsNullOrEmpty(c)) return "—";
            if (c.Length != 44) return c;
            return $"{c[..4]} {c[4..8]} {c[8..12]} {c[12..16]} {c[16..20]} {c[20..24]} {c[24..28]} {c[28..32]} {c[32..36]} {c[36..40]} {c[40..44]}";
        }
        private static string FormatarCnpj(string v)
        {
            v = new string(v.Where(char.IsDigit).ToArray());
            return v.Length == 14
                ? $"{v[..2]}.{v[2..5]}.{v[5..8]}/{v[8..12]}-{v[12..]}"
                : v;
        }
        private static string FormatarDocumento(string v)
        {
            v = new string(v.Where(char.IsDigit).ToArray());
            if (v.Length == 11) return $"{v[..3]}.{v[3..6]}.{v[6..9]}-{v[9..]}";
            if (v.Length == 14) return FormatarCnpj(v);
            return v;
        }
        private static string FormatarFone(string v)
        {
            if (string.IsNullOrEmpty(v)) return "—";
            v = new string(v.Where(char.IsDigit).ToArray());
            return v.Length == 11
                ? $"({v[..2]}) {v[2..7]}-{v[7..]}"
                : v.Length == 10
                    ? $"({v[..2]}) {v[2..6]}-{v[6..]}"
                    : v;
        }
        private static string MontarEndereco(string logr, string num, string bairro, string cep)
        {
            var partes = new[] { logr, string.IsNullOrEmpty(num) ? "" : $"nº {num}", bairro, cep }
                .Where(p => !string.IsNullOrWhiteSpace(p));
            string r = string.Join(", ", partes);
            return string.IsNullOrWhiteSpace(r) ? "—" : r;
        }
    }
}
