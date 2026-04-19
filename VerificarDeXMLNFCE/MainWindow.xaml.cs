using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VerificarDeXMLNFCE
{
    public partial class MainWindow : Window
    {
        // ─── Estado ──────────────────────────────────────────────────────────────
        private ObservableCollection<NotaFiscalItem> _todosItens = new();
        private ObservableCollection<NotaFiscalItem> _itensFiltrados = new();
        private CancellationTokenSource? _cts;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Combo filtro
            cbFiltro.Items.Add("Todos");
            cbFiltro.Items.Add("✅  Com Pagamento");
            cbFiltro.Items.Add("❌  Sem Pagamento");
            cbFiltro.Items.Add("⚠️  Erro / Pendente");
            cbFiltro.SelectedIndex = 0;

            dgResultados.ItemsSource = _itensFiltrados;
            AtualizarInterface();
        }

        // ─── Modo de entrada ─────────────────────────────────────────────────────
        private void ModoChanged(object sender, RoutedEventArgs e)
        {
            if (painelPasta == null) return;
            bool isPasta = rbPasta.IsChecked == true;
            painelPasta.Visibility  = isPasta ? Visibility.Visible : Visibility.Collapsed;
            painelChaves.Visibility = isPasta ? Visibility.Collapsed : Visibility.Visible;
        }

        // ─── Selecionar pasta ────────────────────────────────────────────────────
        private void BtnSelecionarPasta_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                CheckFileExists = false,
                FileName = "Selecione uma pasta"
            };

            if (dialog.ShowDialog() == true)
            {
                txtPasta.Text = Path.GetDirectoryName(dialog.FileName);
            }
        }
        private void DgResultados_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgResultados.SelectedItem is not NotaFiscalItem item) return;

            var menu = new ContextMenu();

            var copiarChave = new MenuItem { Header = "📋  Copiar Chave de Acesso" };
            copiarChave.Click += (_, _) =>
            {
                Clipboard.SetText(item.ChaveCompleta);
                SetStatus("Chave copiada!", "#22C55E", "");
            };

            var copiarEmitente = new MenuItem { Header = "🏢  Copiar CNPJ Emitente" };
            copiarEmitente.Click += (_, _) =>
            {
                Clipboard.SetText(item.Emitente);
                SetStatus("CNPJ copiado!", "#22C55E", "");
            };

            var copiarValor = new MenuItem { Header = "💰  Copiar Valor Total" };
            copiarValor.Click += (_, _) =>
            {
                Clipboard.SetText(item.ValorTotal);
                SetStatus("Valor copiado!", "#22C55E", "");
            };

            var copiarData = new MenuItem { Header = "📅  Copiar Data Emissão" };
            copiarData.Click += (_, _) =>
            {
                Clipboard.SetText(item.DataEmissao);
                SetStatus("Data copiada!", "#22C55E", "");
            };

            var copiarLinha = new MenuItem { Header = "📄  Copiar Linha Completa" };
            copiarLinha.Click += (_, _) =>
            {
                string linha = $"{item.ChaveCompleta}\t{item.Emitente}\t{item.DataEmissao}\t{item.DataPagamento}\t{item.ValorTotal}\t{item.Observacao}";
                Clipboard.SetText(linha);
                SetStatus("Linha completa copiada!", "#22C55E", "");
            };
            var abrirNoSite = new MenuItem { Header = "🌐  Abrir no site SEFAZ" };
            abrirNoSite.Click += (_, _) =>
            {
                AbrirNotaNoNavegador(item.ChaveCompleta);
            };

            menu.Items.Add(copiarChave);
            menu.Items.Add(copiarEmitente);
            menu.Items.Add(copiarValor);
            menu.Items.Add(copiarData);
            menu.Items.Add(new Separator());
            menu.Items.Add(abrirNoSite);
            menu.Items.Add(copiarLinha);

            menu.IsOpen = true;
        }
        // ─── CONSULTAR ───────────────────────────────────────────────────────────
        private async void BtnConsultar_Click(object sender, RoutedEventArgs e)
        {
            var itens = new List<(string chave, string fonte)>();

            if (rbPasta.IsChecked == true)
            {
                var pasta = txtPasta.Text.Trim();
                if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
                {
                    MsgErro("Selecione uma pasta válida."); return;
                }
                var xmls = Directory.GetFiles(pasta, "*.xml", SearchOption.TopDirectoryOnly);
                if (xmls.Length == 0) { MsgErro("Nenhum XML encontrado na pasta selecionada."); return; }

                foreach (var f in xmls)
                    itens.Add((f, Path.GetFileName(f)));
            }
            else
            {
                var linhas = txtChaves.Text
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim().Replace(" ", ""))
                    .Where(l => l.Length > 0)
                    .Distinct()
                    .ToList();

                if (linhas.Count == 0) { MsgErro("Informe ao menos uma chave ou número de NF."); return; }
                foreach (var l in linhas) itens.Add((l, "Manual"));
            }

            // Preparar UI
            _cts = new CancellationTokenSource();
            _todosItens.Clear();
            _itensFiltrados.Clear();
            SetProgresso(true, 0, $"0 / {itens.Count}", "Iniciando…");
            btnConsultar.IsEnabled = false;
            btnExportar.IsEnabled  = false;
            overlayVazio.Visibility = Visibility.Collapsed;
            AtualizarCards();

            int processados = 0;
            double total = itens.Count;

            try
            {
                await Task.Run(async () =>
                {
                    for (int i = 0; i < itens.Count; i++)
                    {
                        _cts.Token.ThrowIfCancellationRequested();

                        var (entrada, fonte) = itens[i];
                        NotaFiscalItem item;
                        try
                        {
                            NfceInfo info;
                            if (File.Exists(entrada))
                            {
                                info = XmlParser.ParseXml(entrada);
                                info.Fonte = "XML Local";

                                var detalhe = XmlParserDetalhe.ParseDetalhado(entrada);
                                info.StatusSefaz = detalhe.StatusSefaz;
                                info.Observacao = detalhe.Observacao;
                            }
                            else if (entrada.Length == 44 && entrada.All(char.IsDigit))
                            {
                                info = new NfceInfo { ChaveAcesso = entrada, Fonte = "Chave Manual" };
                            }
                            else
                            {
                                info = new NfceInfo { NumeroNF = entrada, Fonte = "Número Manual" };
                            }

                            if (!string.IsNullOrEmpty(info.ChaveAcesso))
                            {
                                var resultado = await SefazConsulta.ConsultarAsync(info.ChaveAcesso, _cts.Token);
                                info.DataPagamento = resultado.DataPagamento;

                                // Se o XML não tinha protNFe (Erro = sem protocolo),
                                // usa o resultado do SEFAZ para definir o status
                                if (info.StatusSefaz == StatusConsulta.Erro)
                                {
                                    // 🚨 SE CAPTCHA → NÃO SOBRESCREVE
                                    if (resultado.Observacao?.Contains("CAPTCHA") == true)
                                    {
                                        info.StatusSefaz = StatusConsulta.Erro;
                                        info.Observacao = "⚠ Nota pode estar emitida, mas SEFAZ bloqueou (CAPTCHA)";
                                    }
                                    else
                                    {
                                        info.StatusSefaz = resultado.Status;
                                        info.Observacao = resultado.Observacao;
                                    }
                                }
                            }
                            else
                            {
                                info.StatusSefaz = StatusConsulta.SemChave;
                                info.Observacao = "Sem chave de acesso para consultar";
                            }

                            item = NotaFiscalItem.FromInfo(i + 1, info);
                        }


                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            item = NotaFiscalItem.Erro(i + 1, entrada, fonte, ex.Message);
                        }

                        Dispatcher.Invoke(() =>
                        {
                            _todosItens.Add(item);
                            AplicarFiltro();
                            AtualizarCards();
                            processados++;
                            double pct = (processados / total) * 100;
                            SetProgresso(true, pct,
                                $"{processados} / {(int)total}",
                                $"Processando: {item.ChaveResumida}");
                        });

                        // Pequena pausa para não sobrecarregar o SEFAZ
                        await Task.Delay(800, _cts.Token).ContinueWith(_ => { });
                    }
                }, _cts.Token);
            }
            catch (OperationCanceledException)
            {
                SetStatus("Consulta cancelada pelo usuário.", "#EAB308", "#713F12");
            }
            finally
            {
                SetProgresso(false);
                btnConsultar.IsEnabled = true;
                btnExportar.IsEnabled  = _todosItens.Count > 0;
                overlayVazio.Visibility = _itensFiltrados.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                SetStatus($"Concluído: {_todosItens.Count} nota(s) processada(s).", "#22C55E", "#0F2A1A");
            }
        }

        private void BtnCancelar_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

        // ─── Limpar ──────────────────────────────────────────────────────────────
        private void BtnLimpar_Click(object sender, RoutedEventArgs e)
        {
            _todosItens.Clear();
            _itensFiltrados.Clear();
            txtPasta.Text  = "";
            txtChaves.Text = "";
            cbFiltro.SelectedIndex = 0;
            btnExportar.IsEnabled  = false;
            overlayVazio.Visibility = Visibility.Visible;
            AtualizarCards();
            SetStatus("Pronto para consultar.", "#22C55E", "#0F2A1A");
        }

        // ─── Exportar CSV ────────────────────────────────────────────────────────
        private void BtnExportar_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Exportar resultado",
                Filter     = "CSV (*.csv)|*.csv",
                FileName   = $"nfce_{DateTime.Now:yyyyMMdd_HHmm}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("\"#\",\"Status\",\"Chave\",\"Emitente\",\"Data Emissao\",\"Data Pagamento\",\"Valor\",\"Fonte\",\"Observacao\"");

            foreach (var it in _itensFiltrados)
                sb.AppendLine($"\"{it.Index}\",\"{it.StatusLabel}\",\"{it.ChaveResumida}\"," +
                              $"\"{it.Emitente}\",\"{it.DataEmissao}\",\"{it.DataPagamento}\"," +
                              $"\"{it.ValorTotal}\",\"{it.Fonte}\",\"{it.Observacao}\"");

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            MessageBox.Show($"Exportado com sucesso:\n{dlg.FileName}", "Exportação", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ─── Filtro ──────────────────────────────────────────────────────────────
        private void FiltroChanged(object sender, SelectionChangedEventArgs e) => AplicarFiltro();

        private void AplicarFiltro()
        {
            if (_todosItens == null) return;
            int sel = cbFiltro.SelectedIndex;
            var filtrado = sel switch
            {
                1 => _todosItens.Where(i => i.Status == StatusConsulta.ComPagamento),
                2 => _todosItens.Where(i => i.Status == StatusConsulta.SemPagamento),
                3 => _todosItens.Where(i => i.Status is StatusConsulta.Erro or StatusConsulta.SemChave),
                _ => _todosItens.AsEnumerable()
            };

            _itensFiltrados.Clear();
            foreach (var it in filtrado) _itensFiltrados.Add(it);

            lblContagem.Text = $"{_itensFiltrados.Count} registro(s)";
            overlayVazio.Visibility = _itensFiltrados.Count == 0 && _todosItens.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── Helpers de UI ──────────────────────────────────────────────────────
        private void AtualizarCards()
        {
            lblTotal.Text         = _todosItens.Count.ToString();
            lblComPagamento.Text  = _todosItens.Count(i => i.Status == StatusConsulta.ComPagamento).ToString();
            lblSemPagamento.Text  = _todosItens.Count(i => i.Status == StatusConsulta.SemPagamento).ToString();
            lblErros.Text         = _todosItens.Count(i => i.Status is StatusConsulta.Erro or StatusConsulta.SemChave).ToString();
        }
        private void AbrirNotaNoNavegador(string chave)
        {
            if (string.IsNullOrWhiteSpace(chave) || chave.Length != 44)
            {
                SetStatus("Chave inválida.", "#EF4444", "");
                return;
            }

            string uf = chave.Substring(0, 2);
            string url = "";

            switch (uf)
            {
                case "35": // SP
                    url = $"https://www.nfce.fazenda.sp.gov.br/consultaPublica/Paginas/ConsultaPublica.aspx?p={chave}";
                    break;

                case "41": // PR
                    url = $"https://www.fazenda.pr.gov.br/nfce/qrcode?p={chave}";
                    break;

                case "43": // RS
                    url = $"https://www.sefaz.rs.gov.br/NFCE/NFCE-COM.aspx?p={chave}";
                    break;

                default:
                    SetStatus("UF não suportada ainda.", "#EAB308", "");
                    return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });

                SetStatus("Abrindo no navegador...", "#22C55E", "");
            }
            catch (Exception ex)
            {
                SetStatus("Erro ao abrir: " + ex.Message, "#EF4444", "");
            }
        }
        private void AtualizarInterface() => AtualizarCards();

        private void SetProgresso(bool visivel, double valor = 0, string texto = "", string sub = "")
        {
            overlayProgresso.Visibility = visivel ? Visibility.Visible : Visibility.Collapsed;
            progressBar.Value      = valor;
            lblProgresso.Text      = $"Processando… {texto}";
            lblSubProgresso.Text   = sub;
        }

        private void SetStatus(string msg, string dotColor, string _ = "")
        {
            lblStatus.Text = msg;
            statusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(dotColor));
        }

        // ─── Abrir janela de consulta individual ─────────────────────────────
        private void BtnConsultaIndividual_Click(object sender, RoutedEventArgs e)
        {
            var janela = new ConsultaNotaWindow { Owner = this };
            janela.Show();
        }

        // ─── Clique duplo na linha → abre detalhe ────────────────────────────
        private void DgResultados_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (dgResultados.SelectedItem is not NotaFiscalItem item) return;
            if (string.IsNullOrEmpty(item.ChaveCompleta)) return;

            var janela = new ConsultaNotaWindow { Owner = this };
            janela.Show();
            // Preenche a chave e já dispara a consulta
            janela.AbrirComChave(item.ChaveCompleta);
        }

        private static void MsgErro(string msg) =>
            MessageBox.Show(msg, "Atenção", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
