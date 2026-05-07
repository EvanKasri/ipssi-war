using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// NetworkTransform avec autorité côté owner (client).
/// Le propriétaire du tank contrôle sa position — elle est ensuite
/// synchronisée vers le serveur et les autres clients.
/// Remplace NetworkTransform sur le prefab Tank.
/// </summary>
[DisallowMultipleComponent]
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
