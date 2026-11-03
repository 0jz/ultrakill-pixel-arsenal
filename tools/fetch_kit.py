import os, urllib.request
B = 'https://melty.gg/kit/net/'
A = 'addons/adapters/unity-bepinex/'
G = 'addons/games/ultrakill/'
files = ['MeltyNet.cs', 'MeltyStart.cs', 'README.md', 'protocol.md', 'addons/common/MeltyAddon.cs']
files += [A + f for f in '''AnimPack.cs AnimPackCodec.cs AutoFind.cs GameSession.cs GroundStand.cs MeltyNet.Unity.csproj MeltyUnityNet.Auto.cs MeltyUnityNet.Carry.cs MeltyUnityNet.Gear.cs MeltyUnityNet.Ground.cs MeltyUnityNet.Input.cs MeltyUnityNet.Lab.cs MeltyUnityNet.Netcode.cs MeltyUnityNet.Party.cs MeltyUnityNet.PartyView.cs MeltyUnityNet.Pilot.cs MeltyUnityNet.Screen.cs MeltyUnityNet.Shared.cs MeltyUnityNet.SharedHost.cs MeltyUnityNet.Sounds.cs MeltyUnityNet.Spawners.cs MeltyUnityNet.Start.cs MeltyUnityNet.State.cs MeltyUnityNet.Story.cs MeltyUnityNet.World.cs MeltyUnityNet.cs MirrorCarry.cs NetConfig.cs NgoCarry.cs PhotonCarry.cs PhotonRoomServer.cs PhotonWire.cs Puppets.cs README.md StoryRules.cs THIRD-PARTY.md UnityPauseKeeper.cs UnityShared.Spawners.cs UnityShared.cs UnitySharedHost.cs UnityStoryLayer.cs UnityWorldLayer.cs'''.split()]
files += [G + f for f in '''MeltyNet.Ultrakill.csproj MeltyNetUltrakill.Gear.cs MeltyNetUltrakill.Input.cs MeltyNetUltrakill.Sounds.cs MeltyNetUltrakill.Start.cs MeltyNetUltrakill.cs MeltyTarget.cs README.md SharedPatches.cs SharedWorld.cs melty-net.cfg profile.json'''.split()]
for f in files:
    out = os.path.join('work/meltykit', f)
    os.makedirs(os.path.dirname(out), exist_ok=True)
    try:
        data = urllib.request.urlopen(B + f).read()
    except Exception as e:
        print('FAILED', f, e)
        continue
    open(out, 'wb').write(data)
# the adapter's build looks for the kit's client and start helper in addons/kit
os.makedirs('work/meltykit/addons/kit', exist_ok=True)
for f in ('MeltyNet.cs', 'MeltyStart.cs'):
    open('work/meltykit/addons/kit/' + f, 'wb').write(open('work/meltykit/' + f, 'rb').read())
print(len(files), 'done')
