using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una riga della griglia dei documenti di una cartella, con i suoi tre pulsanti.</summary>
public partial class DocumentoViewModel(DocumentoDettaglio dettaglio, bool fileMancante, CartellaFormViewModel form)
    : ObservableObject, IOrigineDocumento, IDocumentoAnteprima
{
    public int Id { get; } = dettaglio.Id;
    int IOrigineDocumento.DocumentoId => Id;
    int IOrigineDocumento.CartellaOrigineId => form.Id;
    public string NomeFile { get; } = dettaglio.NomeFile;

    /// <summary>"PDF", "DOCX"...</summary>
    public string Tipo { get; } = dettaglio.Estensione.TrimStart('.').ToUpperInvariant();

    public string DimensioneTesto { get; } = FormatiTesto.Dimensione(dettaglio.Dimensione);
    public DateTime DataCaricamento { get; } = dettaglio.DataCaricamento;

    [ObservableProperty]
    private string _percorsoRelativo = dettaglio.PercorsoRelativo;

    /// <summary>Il file non si trova più sul disco (spostato o eliminato da Esplora file).</summary>
    [ObservableProperty]
    private bool _fileMancante = fileMancante;

    [RelayCommand]
    private void Apri() => form.ApriDocumento(this);

    [RelayCommand]
    private void ApriNellaCartella() => form.MostraDocumentoInEsplora(this);

    [RelayCommand]
    private Task EliminaAsync() => form.EliminaDocumentoAsync(this);
}
