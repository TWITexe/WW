using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Mirror;
using UnityEngine;

// Password proof is bound to a fresh challenge; discovery never contains credentials.
public class RoomAuthenticator : NetworkAuthenticator
{
    public string ServerKey { get; set; } = "";
    public string ClientKey { get; set; } = "";
    public static string PlayerId { get; private set; } = Guid.NewGuid().ToString("N");
    public static string LastError { get; set; }
    readonly Dictionary<NetworkConnectionToClient, string> challenges = new();
    readonly Dictionary<NetworkConnectionToClient, ShopProfile> profiles = new();
    public ShopProfile ProfileFor(NetworkConnectionToClient conn) => conn != null && profiles.TryGetValue(conn, out var profile) ? profile : null;
    public void Forget(NetworkConnectionToClient conn) { profiles.Remove(conn); challenges.Remove(conn); }
    public struct Challenge : NetworkMessage { public string nonce; }
    public struct Proof : NetworkMessage { public string playerId; public string signature; public string inventory; }
    public struct Result : NetworkMessage { public bool accepted; public string error; }
    public static string PasswordKey(string password)
    {
        if (string.IsNullOrEmpty(password)) return "";
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(password)));
    }
    public static string Sign(string key, string nonce, string playerId)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key ?? ""));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(nonce + ":" + playerId)));
    }
    public override void OnStartServer() => NetworkServer.RegisterHandler<Proof>(OnProof, false);
    public override void OnStopServer()
    {
        NetworkServer.UnregisterHandler<Proof>(); challenges.Clear(); profiles.Clear(); StopAllCoroutines();
    }
    public override void OnServerAuthenticate(NetworkConnectionToClient conn)
    {
        string nonce = Guid.NewGuid().ToString("N");
        challenges[conn] = nonce;
        conn.Send(new Challenge { nonce = nonce });
        StartCoroutine(Timeout(conn));
    }
    IEnumerator Timeout(NetworkConnectionToClient conn)
    {
        yield return new WaitForSecondsRealtime(20);
        if (challenges.Remove(conn)) ServerReject(conn);
    }
    void OnProof(NetworkConnectionToClient conn, Proof proof)
    {
        if (conn.isAuthenticated || !challenges.TryGetValue(conn, out string nonce)) return;
        challenges.Remove(conn);
        VerifyProof(conn, proof, nonce);
    }
    void VerifyProof(NetworkConnectionToClient conn, Proof proof, string nonce)
    {
        bool accepted = Guid.TryParseExact(proof.playerId, "N", out _) && proof.signature == Sign(ServerKey, nonce, proof.playerId);
        ShopProfile verified = null;
        if (accepted)
        {
            try
            {
                if (proof.inventory == null || proof.inventory.Length > 8192) accepted = false;
                else
                {
                    var local = JsonUtility.FromJson<ShopProfile>(proof.inventory);
                    if (local == null || local.playerId != proof.playerId) accepted = false;
                    else
                    {
                        // Local saves are deliberately client-owned. Only known item IDs reach gameplay.
                        verified = new ShopProfile { playerId = proof.playerId,
                            owned = (local.owned ?? Array.Empty<string>()).Where(id => ShopCatalog.Find(id) != null).Distinct().ToArray() };
                        verified.hat = local.hat; verified.staff = local.staff; verified.body = local.body;
                    }
                }
            }
            catch (ArgumentException) { accepted = false; }
        }
        if (!NetworkServer.active || !NetworkServer.connections.TryGetValue(conn.connectionId, out var current) || current != conn) return;
        if (accepted)
            foreach (var other in NetworkServer.connections.Values)
                if (other != conn && other.isAuthenticated && (string)other.authenticationData == proof.playerId) accepted = false;
        conn.Send(new Result { accepted = accepted, error = accepted ? "" : "Вход не подтверждён. Проверьте пароль комнаты и повторный вход того же игрока." });
        if (accepted) { conn.authenticationData = proof.playerId; if (verified != null) profiles[conn] = verified; ServerAccept(conn); }
        else StartCoroutine(Reject(conn));
    }
    IEnumerator Reject(NetworkConnectionToClient conn)
    {
        yield return new WaitForSecondsRealtime(.5f); ServerReject(conn);
    }
    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<Challenge>(SendProof, false);
        NetworkClient.RegisterHandler<Result>(result =>
        {
            if (result.accepted) { LastError = null; ClientAccept(); }
            else { LastError = result.error; ClientReject(); }
        }, false);
    }
    void SendProof(Challenge challenge)
    {
        if (!NetworkClient.isConnected) return;
        var local = EconomyClient.Instance?.Profile;
        if (local != null) PlayerId = local.playerId;
        var inventory = new ShopProfile { playerId = PlayerId, owned = local?.owned ?? Array.Empty<string>(),
            hat = local?.hat, staff = local?.staff, body = local?.body };
        NetworkClient.Send(new Proof { playerId = PlayerId, signature = Sign(ClientKey, challenge.nonce, PlayerId), inventory = JsonUtility.ToJson(inventory) });
    }
    public override void OnClientAuthenticate() { }
    public override void OnStopClient()
    {
        NetworkClient.UnregisterHandler<Challenge>(); NetworkClient.UnregisterHandler<Result>(); ClientKey = "";
    }
}
