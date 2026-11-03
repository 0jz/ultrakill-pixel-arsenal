// Pixel Arsenal's side door into Melty Net's ULTRAKILL add-on; Pixel Arsenal fills these by name (src/NetBridge.cs).
using System;
using UnityEngine;

public static class PixelArsenalBridge
{
    public static Func<GameObject, string, bool> ShowHeld; // a puppet, what its player holds: true when it's shown
    public static Func<GameObject, bool> HasGun;           // the puppet holds a PG3D gun: V2's stand-in gun stays away
    public static Action<GameObject> Shot;                 // the puppet's player fired
    public static Action<string> Fired;                    // this player fired (ACT in the room's recording)
    public static int Others;                              // other players in the room
}
