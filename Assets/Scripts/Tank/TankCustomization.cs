using UnityEngine;

/// <summary>
/// IPSSI-WAR : palette de couleurs pour la personnalisation du tank.
/// L'index choisi par le joueur est stocke ici cote local, puis transmis au
/// serveur via TankSkin (LocalPlayerSetup) pour synchronisation.
/// </summary>
public static class TankCustomization
{
    // Palette fixe — facile a etendre.
    public static readonly Color[] Palette = new Color[]
    {
        new Color(1.00f, 0.50f, 0.00f), // 0 Orange (defaut)
        new Color(0.85f, 0.18f, 0.18f), // 1 Rouge
        new Color(0.18f, 0.45f, 0.85f), // 2 Bleu
        new Color(0.20f, 0.70f, 0.30f), // 3 Vert
        new Color(0.95f, 0.85f, 0.10f), // 4 Jaune
        new Color(0.62f, 0.30f, 0.85f), // 5 Violet
        new Color(0.10f, 0.80f, 0.78f), // 6 Cyan
        new Color(0.92f, 0.92f, 0.92f), // 7 Blanc
    };

    public static readonly string[] PaletteNames =
    {
        "Orange", "Rouge", "Bleu", "Vert", "Jaune", "Violet", "Cyan", "Blanc"
    };

    /// <summary>Index choisi par le joueur dans le menu (persistant runtime).</summary>
    public static int SelectedColorIndex = 0;

    /// <summary>Couleur appliquee aux tanks-bots cote serveur (toujours rouge).</summary>
    public const int BotColorIndex = 1;

    public static int ClampIndex(int i) => Mathf.Clamp(i, 0, Palette.Length - 1);
    public static Color GetColor(int i) => Palette[ClampIndex(i)];
}
