namespace DocumentaleMarta.App.ViewModels;

public static class FormatiTesto
{
    /// <summary>Dimensione leggibile: "850 B", "12,5 KB", "3,2 MB"...</summary>
    public static string Dimensione(long byte_)
    {
        string[] unita = ["B", "KB", "MB", "GB", "TB"];
        double valore = byte_;
        var i = 0;
        while (valore >= 1024 && i < unita.Length - 1)
        {
            valore /= 1024;
            i++;
        }
        return i == 0 ? $"{byte_} B" : $"{valore:0.#} {unita[i]}";
    }
}
