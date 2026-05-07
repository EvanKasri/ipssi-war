using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Posé sur le NetworkManager (DontDestroyOnLoad).
/// Détecte quand la connexion réseau se coupe (déconnexion ou shutdown),
/// puis recharge la scène pour afficher le menu IPSSI-WAR.
/// </summary>
public class NetworkSessionWatcher : MonoBehaviour
{
    private bool _wasConnected = false;
    private bool _reloadScheduled = false;

    private void Update()
    {
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm == null) return;

        bool isConnected = nm.IsListening;

        if (!_wasConnected && isConnected)
        {
            // Vient de se connecter
            _wasConnected = true;
            _reloadScheduled = false;
        }

        if (_wasConnected && !isConnected && !_reloadScheduled)
        {
            // Était connecté, maintenant déconnecté → retour au menu
            _wasConnected = false;
            _reloadScheduled = true;
            StartCoroutine(ReloadScene());
        }
    }

    private System.Collections.IEnumerator ReloadScene()
    {
        // Petite attente pour laisser NGO terminer son cleanup
        yield return new WaitForSeconds(0.3f);
        _reloadScheduled = false;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
