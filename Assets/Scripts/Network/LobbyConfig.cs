/// <summary>
/// Configuration partagée du lobby entre le menu et le GameManager.
/// </summary>
public static class LobbyConfig
{
    /// <summary>Nombre de joueurs requis pour lancer la partie (2, 3 ou 4).</summary>
    public static int RequiredPlayers = 2;

    /// <summary>Secondes avant que le lobby expire si les joueurs ne rejoignent pas.</summary>
    public static float LobbyTimeoutSeconds = 120f;

    /// <summary>Si true, le GameManager spawne un tank-bot adverse (mode 1 joueur).</summary>
    public static bool BotMode = false;

    /// <summary>
    /// IPSSI-WAR : mode One Shot — charge complete obligatoire et un seul impact tue.
    /// Active cote serveur (ShellExplosion) et cote owner (TankShooting bloque le tir
    /// premature avant charge max).
    /// </summary>
    public static bool OneShotMode = false;
}
