using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SiegeSandbox.Tests
{
    /// <summary>Play mode checks for the core mechanics, driven through the same input struct the keyboard fills.</summary>
    public class MechanicsTests
    {
        PlayerController player;
        Weapon weapon;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync("Sandbox", LoadSceneMode.Single);
            yield return null;
            player = PlayerController.Instance;
            weapon = Weapon.Instance;
            Assert.IsNotNull(player, "no PlayerController in the Sandbox scene");
            Assert.IsNotNull(weapon, "no Weapon in the Sandbox scene");
            player.useDeviceInput = false;
            player.input = new PlayerInputState();
            yield return null;
        }

        IEnumerator TapCrouch()
        {
            PlayerInputState s = player.input;
            s.crouchPressed = true;
            player.input = s;
            yield return null;
            s.crouchPressed = false;
            player.input = s;
        }

        IEnumerator TapProne()
        {
            PlayerInputState s = player.input;
            s.pronePressed = true;
            player.input = s;
            yield return null;
            s.pronePressed = false;
            player.input = s;
        }

        [UnityTest]
        public IEnumerator Stances_ReshapeTheHitbox()
        {
            Bounds stand = player.GetHitboxBounds();

            yield return TapCrouch();
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(Stance.Crouch, player.Stance);
            Bounds crouch = player.GetHitboxBounds();
            Assert.Less(crouch.size.y, stand.size.y - 0.4f, "crouching should make the hitbox clearly shorter");

            yield return TapProne();
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(Stance.Prone, player.Stance);
            Bounds prone = player.GetHitboxBounds();
            Assert.Less(prone.size.y, crouch.size.y - 0.3f, "prone should be lower than crouch");
            Assert.Greater(prone.size.z, 1.5f, "prone should stretch the hitbox out along the body");

            yield return TapProne();
            yield return new WaitForSeconds(0.9f);
            Assert.AreEqual(Stance.Stand, player.Stance);
            Assert.AreEqual(stand.size.y, player.GetHitboxBounds().size.y, 0.05f);
        }

        [UnityTest]
        public IEnumerator Lean_MovesHeadAndCameraButNotLegs()
        {
            PlayerInputState s = player.input;
            s.lean = 1f;
            player.input = s;
            yield return new WaitForSeconds(0.6f);

            Transform t = player.transform;
            Assert.Greater(player.Lean, 0.9f);
            Assert.Greater(t.InverseTransformPoint(player.headHitbox.position).x, 0.15f, "head hitbox should swing right");
            Assert.Greater(t.InverseTransformPoint(player.cam.transform.position).x, 0.15f, "camera should swing right");
            Assert.AreEqual(0f, t.InverseTransformPoint(player.legsHitbox.position).x, 0.01f, "legs stay put");

            s.lean = -1f;
            player.input = s;
            yield return new WaitForSeconds(0.8f);
            Assert.Less(t.InverseTransformPoint(player.headHitbox.position).x, -0.15f, "head hitbox should swing left");
        }

        [UnityTest]
        public IEnumerator Bullets_TakeTimeToArrive()
        {
            GameObject go = new GameObject("TestTarget");
            go.transform.position = new Vector3(0f, 40f, 60f);
            go.AddComponent<SphereCollider>().radius = 1f;
            Target target = go.AddComponent<Target>();
            yield return null;

            // 59 m to the surface at 60 m/s: about a second of flight.
            float t0 = Time.time;
            Projectile.Spawn(new Vector3(0f, 40f, 0f), Vector3.forward * 60f, 34f, 0f, null, null);
            while (target.Health >= target.maxHealth && Time.time - t0 < 3f) yield return null;

            float flight = Time.time - t0;
            Assert.Less(target.Health, target.maxHealth, "the bullet never arrived");
            Assert.Greater(flight, 0.6f, "the hit was (nearly) instant, so this is not a projectile");
            Assert.Less(flight, 1.6f);
        }

        [UnityTest]
        public IEnumerator Recoil_FollowsThePattern_AndAdsTamesIt()
        {
            // Hip-fire spray.
            float pitch0 = player.Pitch, yaw0 = player.Yaw;
            int shots0 = weapon.ShotsFired;
            PlayerInputState s = player.input;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.5f);
            s.fire = false;
            player.input = s;
            yield return new WaitForSeconds(0.6f);

            int n = weapon.ShotsFired - shots0;
            Assert.Greater(n, 3, "the weapon barely fired");
            Assert.AreEqual(weapon.magazineSize - n, weapon.Ammo);
            Vector2 expected = Vector2.zero;
            for (int i = 0; i < n; i++) expected += weapon.GetRecoilKick(i, 0f, Stance.Stand);
            Assert.AreEqual(expected.y, player.Pitch - pitch0, 0.05f, "vertical kick should equal the pattern");
            Assert.AreEqual(expected.x, player.Yaw - yaw0, 0.05f, "horizontal kick should equal the pattern");
            float hipPerShot = (player.Pitch - pitch0) / n;

            // Same thing aimed down sights.
            s.aim = true;
            player.input = s;
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(1f, weapon.AimBlend, 0.001f);
            Assert.AreEqual(weapon.adsFov, player.cam.fieldOfView, 0.1f, "ADS should zoom the camera");
            Assert.Less(weapon.CurrentSpreadDeg, 0.1f, "ADS should be nearly perfectly accurate");

            pitch0 = player.Pitch;
            shots0 = weapon.ShotsFired;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.5f);
            s.fire = false;
            player.input = s;
            yield return new WaitForSeconds(0.6f);

            n = weapon.ShotsFired - shots0;
            Assert.Greater(n, 3);
            float adsPerShot = (player.Pitch - pitch0) / n;
            Assert.Less(adsPerShot, hipPerShot * 0.75f, "ADS should reduce recoil");
        }

        [Test]
        public void SpreadRules()
        {
            Weapon w = new GameObject("RulesWeapon").AddComponent<Weapon>();
            float hip = w.GetSpread(0f, 0f, 0f, Stance.Stand);
            Assert.Less(w.GetSpread(1f, 0f, 0f, Stance.Stand), hip * 0.2f, "ADS tightens the cone");
            Assert.Greater(w.GetSpread(0f, 1f, 0f, Stance.Stand), hip, "sustained fire widens the hip cone");
            Assert.Greater(w.GetSpread(0f, 0f, 1f, Stance.Stand), hip, "moving widens the hip cone");
            Assert.Less(w.GetSpread(0f, 0f, 0f, Stance.Crouch), hip, "crouching steadies the weapon");
            for (int i = 0; i < 40; i++)
                Assert.AreEqual(w.GetRecoilKick(i, 0f, Stance.Stand), w.GetRecoilKick(i, 0f, Stance.Stand));
            Object.Destroy(w.gameObject);
        }

        IEnumerator Tap(System.Func<PlayerInputState, PlayerInputState> press)
        {
            PlayerInputState held = player.input;
            player.input = press(held);
            yield return null;
            player.input = held;
        }

        [UnityTest]
        public IEnumerator Jump_LeavesTheGround_HurtsAccuracy_AndLands()
        {
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(player.IsGrounded, "player should start on the floor");
            float y0 = player.transform.position.y;
            float groundSpread = weapon.CurrentSpreadDeg;

            yield return Tap(i => { i.jumpPressed = true; return i; });
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(player.IsGrounded);
            Assert.Greater(player.transform.position.y, y0 + 0.4f, "the jump should lift the player");
            Assert.Greater(weapon.CurrentSpreadDeg, groundSpread + 1f, "shooting in the air should be less accurate");

            yield return new WaitForSeconds(1.0f);
            Assert.IsTrue(player.IsGrounded, "the player should have landed");
            Assert.AreEqual(y0, player.transform.position.y, 0.05f);
        }

        [UnityTest]
        public IEnumerator Melee_OnlyHitsWhatIsInReach()
        {
            Target target = GameObject.Find("Target_Spawn").GetComponent<Target>();

            // From the spawn point nothing is close enough.
            yield return Tap(i => { i.meleePressed = true; return i; });
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(0, weapon.MeleeHits);
            Assert.IsTrue(target.IsAlive);

            // Walk up to the dummy and strike it.
            player.Teleport(new Vector3(5f, 0.05f, -23.2f), 0f);
            yield return new WaitForSeconds(0.2f);
            yield return Tap(i => { i.meleePressed = true; return i; });
            Assert.IsTrue(weapon.IsMeleeing);
            yield return new WaitForSeconds(0.6f);
            Assert.AreEqual(1, weapon.MeleeHits);
            Assert.IsFalse(target.IsAlive, "a melee strike should drop the dummy");
            Assert.IsFalse(weapon.IsMeleeing);
        }

        [UnityTest]
        public IEnumerator Reload_SwapsTheMagazine_AndBlocksFiring()
        {
            Vector3 magRest = weapon.magazine.localPosition;
            PlayerInputState s = player.input;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.3f);
            s.fire = false;
            player.input = s;
            yield return new WaitForSeconds(0.1f);
            int before = weapon.Ammo;
            Assert.Less(before, weapon.magazineSize);

            yield return Tap(i => { i.reloadPressed = true; return i; });
            Assert.IsTrue(weapon.IsReloading);

            // Part-way through, the old magazine is out and the rounds are not in yet.
            yield return new WaitForSeconds(0.75f);
            bool magOut = !weapon.magazine.gameObject.activeSelf || weapon.magazine.localPosition.y < magRest.y - 0.05f;
            Assert.IsTrue(magOut, "the magazine should be out of the gun mid-reload");
            Assert.AreEqual(before, weapon.Ammo, "ammo should not refill before the new magazine is seated");

            int shots = weapon.ShotsFired;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.2f);
            s.fire = false;
            player.input = s;
            Assert.AreEqual(shots, weapon.ShotsFired, "the weapon should not fire while reloading");

            float t0 = Time.time;
            while (weapon.IsReloading && Time.time - t0 < 3f) yield return null;
            Assert.IsFalse(weapon.IsReloading);
            Assert.AreEqual(weapon.magazineSize, weapon.Ammo);
            Assert.IsTrue(weapon.magazine.gameObject.activeSelf);
            Assert.Less(Vector3.Distance(magRest, weapon.magazine.localPosition), 0.001f, "the magazine should be back in the gun");
        }

        [UnityTest]
        public IEnumerator Aiming_CancelsSprint()
        {
            PlayerInputState s = player.input;
            s.move = new Vector2(0f, 1f);
            s.sprint = true;
            player.input = s;
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(player.IsSprinting);

            s.aim = true;
            player.input = s;
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(player.IsSprinting, "aiming should drop the player out of a sprint");
            Assert.AreEqual(1f, weapon.AimBlend, 0.001f);
        }

        static void Shoot(Vector3 from, float damage = 34f)
        {
            Projectile.Spawn(from, Vector3.forward * 180f, damage, 0f, null, null);
        }

        [UnityTest]
        public IEnumerator Damage_DependsOnTheBodyPart_AndHeadshotsKill()
        {
            Target target = GameObject.Find("Target_Spawn").GetComponent<Target>();

            Shoot(new Vector3(5f, 1.2f, -25f));            // torso
            yield return new WaitForSeconds(0.3f);
            Assert.IsTrue(target.IsAlive, "one torso shot should not kill");
            Assert.AreEqual(100f - 34f, target.Health, 0.1f);

            Shoot(new Vector3(5.12f, 0.4f, -25f));         // leg
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(100f - 34f - 34f * 0.75f, target.Health, 0.1f, "a leg shot should do three quarters damage");

            Shoot(new Vector3(5.29f, 1.15f, -25f));        // arm
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(100f - 34f - 2f * 34f * 0.75f, target.Health, 0.1f, "an arm shot should do three quarters damage");
            Assert.IsTrue(target.IsAlive);

            yield return new WaitForSeconds(3.5f);         // let a fresh dummy stand in
            target = GameObject.Find("Target_SoftWall").GetComponent<Target>();
            Shoot(new Vector3(-3.3f, 1.68f, -7f));         // head, from behind the soft wall line
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(target.IsAlive, "one headshot should always kill");
        }

        [UnityTest]
        public IEnumerator Damage_FallsOffWithDistance()
        {
            Assert.AreEqual(1f, Projectile.FalloffScale(10f, 18f, 38f, 0.55f), 0.001f);
            Assert.AreEqual(0.775f, Projectile.FalloffScale(28f, 18f, 38f, 0.55f), 0.001f);
            Assert.AreEqual(0.55f, Projectile.FalloffScale(60f, 18f, 38f, 0.55f), 0.001f);

            GameObject nearGo = new GameObject("NearTarget");
            nearGo.transform.position = new Vector3(10f, 40f, 10f);
            nearGo.AddComponent<SphereCollider>().radius = 1f;
            Target near = nearGo.AddComponent<Target>();
            GameObject farGo = new GameObject("FarTarget");
            farGo.transform.position = new Vector3(0f, 40f, 45f);
            farGo.AddComponent<SphereCollider>().radius = 1f;
            Target far = farGo.AddComponent<Target>();
            yield return null;

            Projectile.Spawn(new Vector3(10f, 40f, 0f), Vector3.forward * 180f, 34f, 0f, null, null).SetFalloff(18f, 38f, 0.55f);
            Projectile.Spawn(new Vector3(0f, 40f, 0f), Vector3.forward * 180f, 34f, 0f, null, null).SetFalloff(18f, 38f, 0.55f);
            yield return new WaitForSeconds(0.7f);
            Assert.AreEqual(100f - 34f, near.Health, 0.1f, "full damage inside the fall-off range");
            Assert.AreEqual(100f - 34f * 0.55f, far.Health, 0.1f, "reduced damage at long range");
        }

        [UnityTest]
        public IEnumerator SoftWall_BulletsLeaveSmallHoles_AndHardWallsStopThem()
        {
            SoftWall wall = GameObject.Find("SoftWall").GetComponent<SoftWall>();
            Target behindSoft = GameObject.Find("Target_SoftWall").GetComponent<Target>();
            Vector3 muzzle = new Vector3(-3.3f, 0.9f, -10f);
            Vector3 onWall = new Vector3(-3.3f, 0.9f, -8f);
            Assert.IsFalse(wall.IsOpenAt(onWall));
            Assert.AreEqual(0f, wall.OpenFraction, 0.0001f);

            Shoot(muzzle);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, wall.BulletHoles);
            Assert.IsTrue(wall.IsOpenAt(onWall), "the bullet should leave a hole where it hit");
            Assert.Less(wall.OpenFraction, 0.0006f, "a bullet hole is a few centimetres across");
            Assert.AreEqual(100f - 34f * 0.8f, behindSoft.Health, 0.1f, "punching through costs the bullet some damage");

            // A second bullet through the same hole touches nothing on the way.
            Shoot(muzzle);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, wall.BulletHoles, "no new hole should be made");
            Assert.AreEqual(100f - 34f * 0.8f - 34f, behindSoft.Health, 0.1f, "a bullet through an existing hole keeps full damage");

            GameObject go = new GameObject("BehindHardWall");
            go.transform.position = new Vector3(3f, 1f, -6f);
            go.AddComponent<SphereCollider>().radius = 0.5f;
            Target behindHard = go.AddComponent<Target>();
            yield return null;
            Shoot(new Vector3(3f, 1f, -10f));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(behindHard.maxHealth, behindHard.Health, "a solid wall should stop the bullet");
        }

        [UnityTest]
        public IEnumerator SoftWall_ClusteredBulletsBreakOutAChunk()
        {
            SoftWall wall = GameObject.Find("SoftWall").GetComponent<SoftWall>();

            // Four holes a few centimetres apart: still four separate little holes.
            Vector3 centre = new Vector3(-1.75f, 2.5f, -10f);
            Vector3[] spread = { new Vector3(-0.06f, 0f, 0f), new Vector3(0.06f, 0f, 0f), new Vector3(0f, 0.06f, 0f), new Vector3(0f, -0.06f, 0f) };
            foreach (Vector3 offset in spread) Shoot(centre + offset);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(4, wall.BulletHoles);
            Assert.AreEqual(0, wall.ChunkHoles);
            Assert.IsFalse(wall.IsOpenAt(new Vector3(-1.75f, 2.5f, -8f)), "the sheet between the holes should still be there");
            float before = wall.OpenFraction;

            // The fifth one in the cluster breaks the piece between them out.
            Shoot(centre + new Vector3(0.03f, 0.03f, 0f));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, wall.ChunkHoles, "enough bullets close together should break a chunk out");
            Assert.IsTrue(wall.IsOpenAt(new Vector3(-1.75f, 2.5f, -8f)));
            Assert.Greater(wall.OpenFraction, before * 3f);
        }

        [UnityTest]
        public IEnumerator SoftWall_MeleeKnocksASmallPeekHole()
        {
            SoftWall wall = GameObject.Find("SoftWall").GetComponent<SoftWall>();
            player.Teleport(new Vector3(-2.2f, 0.05f, -9f), 0f);
            yield return new WaitForSeconds(0.2f);
            yield return Tap(k => { k.meleePressed = true; return k; });
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(1, wall.MeleeHoles);
            // Roughly 22 cm across on a 12 square metre wall: about a third of a percent of it.
            Assert.Greater(wall.OpenFraction, 0.0015f, "a melee hole should be clearly bigger than a bullet hole");
            Assert.Less(wall.OpenFraction, 0.008f, "but still a peek hole, not a doorway");
            Assert.IsTrue(wall.IsOpenAt(new Vector3(-2.2f, 1.67f, -8f)));
        }

        [UnityTest]
        public IEnumerator Barricade_BoardsBreakIndividually_AndTwentyBulletsDestroyIt()
        {
            Barricade window = GameObject.Find("Barricade_Window").GetComponent<Barricade>();
            Target behind = GameObject.Find("Target_Window").GetComponent<Target>();
            Assert.IsTrue(window.IsComplete);
            Assert.AreEqual(5, window.PlankCount);

            // One bullet: a small hole, the board stays, and the bullet carries on weaker.
            Vector3 muzzle = new Vector3(-8f, 1.45f, -10f);   // third board up, in line with the dummy
            Shoot(muzzle);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, window.BulletHoles);
            Assert.IsTrue(window.IsComplete, "one bullet should not break a board");
            Assert.AreEqual(100f - 34f * 0.75f, behind.Health, 0.1f);

            // Through that same hole: nothing in the way.
            Shoot(muzzle);
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(1, window.BulletHoles);
            Assert.AreEqual(100f - 34f * 0.75f - 34f, behind.Health, 0.1f);

            // Two more into the same board (elsewhere along it) break that board off, and only that one.
            Shoot(muzzle + new Vector3(-1.0f, 0f, 0f));
            Shoot(muzzle + new Vector3(1.0f, 0f, 0f));
            yield return new WaitForSeconds(0.3f);
            Assert.IsFalse(window.IsPlankPresent(2), "three bullets in one board should break it off");
            Assert.AreEqual(4, window.PlanksPresent, "the other boards should be untouched");

            // Keep shooting the rest: by twenty bullets it is gone.
            float[] rows = { 1.09f, 1.27f, 1.63f, 1.81f };
            for (int i = 0; i < 17 && window.IsBuilt; i++)
            {
                Shoot(new Vector3(-9.2f + 0.14f * i, rows[i % 4], -10f));
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(window.IsBuilt, "about twenty bullets should destroy a barricade");
        }

        [UnityTest]
        public IEnumerator Barricade_ThirdMeleeStrikeAlwaysDestroysIt()
        {
            Barricade window = GameObject.Find("Barricade_Window").GetComponent<Barricade>();
            player.Teleport(new Vector3(-8f, 0.05f, -9f), 0f);
            yield return new WaitForSeconds(0.2f);

            yield return Tap(k => { k.meleePressed = true; return k; });
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(window.IsBuilt);
            Assert.Less(window.PlanksPresent, window.PlankCount, "the first strike should knock boards out");
            int afterOne = window.PlanksPresent;

            yield return Tap(k => { k.meleePressed = true; return k; });
            yield return new WaitForSeconds(0.6f);
            Assert.IsTrue(window.IsBuilt, "two strikes should leave it damaged but standing");
            Assert.Less(window.PlanksPresent, afterOne);

            yield return Tap(k => { k.meleePressed = true; return k; });
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(window.IsBuilt, "the third strike should always bring it down");
        }

        [UnityTest]
        public IEnumerator Barricade_UnrollsWhileHeld_AndIsPriedOffInOnePiece()
        {
            Barricade door = GameObject.Find("Barricade_Door").GetComponent<Barricade>();
            Interactor interactor = player.GetComponent<Interactor>();
            Assert.IsFalse(door.IsBuilt);
            int top = door.PlankCount - 1;

            player.Teleport(new Vector3(0f, 0.05f, -9.3f), 0f);
            yield return new WaitForSeconds(0.2f);
            StringAssert.Contains("barricade", interactor.Prompt);

            // Part-way through it has unrolled from the top but not reached the floor.
            PlayerInputState s = player.input;
            s.interactHeld = true;
            player.input = s;
            yield return new WaitForSeconds(1.0f);
            Assert.IsTrue(interactor.IsBusy);
            Assert.IsTrue(door.IsPlankPresent(top), "it should unroll from the top");
            Assert.IsFalse(door.IsPlankPresent(0), "and not have reached the bottom yet");

            // Let go: it comes away again.
            s.interactHeld = false;
            player.input = s;
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(door.IsBuilt, "an abandoned barricade should not stay up");
            Assert.IsFalse(interactor.IsBusy);

            // Hold it all the way, and try to shoot while doing it.
            int shots = weapon.ShotsFired;
            s.interactHeld = true;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(1.0f);
            Assert.IsTrue(interactor.IsBusy);
            s.fire = false;
            player.input = s;
            Assert.AreEqual(shots, weapon.ShotsFired, "you cannot shoot while building");
            yield return new WaitForSeconds(1.4f);
            Assert.IsTrue(door.IsComplete, "the doorway should now be boarded up");
            Assert.IsFalse(interactor.IsBusy);

            // Still holding the key must not start tearing it down again.
            yield return new WaitForSeconds(0.4f);
            Assert.IsTrue(door.IsComplete);

            // Release, then hold again: nothing comes off until the crowbar finishes, then all of it does.
            s.interactHeld = false;
            player.input = s;
            yield return null;
            s.interactHeld = true;
            player.input = s;
            yield return new WaitForSeconds(0.9f);
            Assert.IsTrue(door.IsComplete, "prying should not remove boards one by one");
            yield return new WaitForSeconds(1.0f);
            s.interactHeld = false;
            player.input = s;
            Assert.IsFalse(door.IsBuilt, "holding the key at a barricade should pry it off");
        }

        [UnityTest]
        public IEnumerator Barricade_LetsYouCrawlUnderOnceTheBottomBoardsAreGone()
        {
            Barricade door = GameObject.Find("Barricade_Door").GetComponent<Barricade>();
            door.BuildAll();
            player.Teleport(new Vector3(0f, 0.05f, -9.0f), 0f);
            yield return new WaitForSeconds(0.2f);
            yield return TapProne();
            yield return new WaitForSeconds(0.6f);

            // Intact: no way under (the gap at the bottom is only big enough for a drone).
            PlayerInputState s = player.input;
            s.move = new Vector2(0f, 1f);
            player.input = s;
            yield return new WaitForSeconds(1.5f);
            Assert.Less(player.transform.position.z, -8.2f, "an intact barricade should stop a crawling player");

            // Take the bottom three boards off and crawl through; that brings the rest down.
            for (int i = 0; i < 3; i++) door.BreakPlank(i, Vector3.forward);
            yield return new WaitForSeconds(2.2f);
            s.move = Vector2.zero;
            player.input = s;
            Assert.Greater(player.transform.position.z, -7.9f, "the player should have crawled under");
            Assert.IsFalse(door.IsBuilt, "going through a damaged barricade destroys what is left");
        }

        [UnityTest]
        public IEnumerator Prone_CrawlsThroughTheTunnel_AndCannotStandInside()
        {
            player.Teleport(new Vector3(8f, 0.05f, -8.9f), 0f);
            yield return new WaitForSeconds(0.2f);
            yield return TapProne();
            yield return new WaitForSeconds(0.6f);

            PlayerInputState s = player.input;
            s.move = new Vector2(0f, 1f);
            player.input = s;
            yield return new WaitForSeconds(1.5f);
            float z = player.transform.position.z;
            Assert.Greater(z, -8.0f, "the prone player should have entered the tunnel");
            Assert.Less(z, -6.2f);

            yield return TapProne();
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(Stance.Prone, player.Stance, "there is no room to get up inside the tunnel");

            yield return new WaitForSeconds(2.8f);
            Assert.Greater(player.transform.position.z, -5.6f, "the player should have crawled out the far side");
            s.move = Vector2.zero;
            player.input = s;
            yield return TapProne();
            yield return new WaitForSeconds(0.9f);
            Assert.AreEqual(Stance.Stand, player.Stance);
        }

        [UnityTest]
        public IEnumerator Lean_PullsRecoilSideways_AndHurtsAccuracyMidLean()
        {
            Vector2 up = new Vector2(0f, 1f);
            Assert.Greater(Weapon.ApplyLeanToKick(up, 10f).x, 0.15f, "leaning right should push vertical recoil right");
            Assert.Less(Weapon.ApplyLeanToKick(up, -10f).x, -0.15f, "leaning left should push it left");
            Assert.AreEqual(1f, Weapon.ApplyLeanToKick(up, 10f).magnitude, 0.001f);

            yield return new WaitForSeconds(0.3f);
            float upright = weapon.CurrentSpreadDeg;
            PlayerInputState s = player.input;
            s.lean = 1f;
            player.input = s;
            yield return new WaitForSeconds(0.07f);
            float midLean = weapon.CurrentSpreadDeg;
            yield return new WaitForSeconds(0.7f);
            float leaned = weapon.CurrentSpreadDeg;
            Assert.Greater(leaned, upright + 0.3f, "hip fire is a little worse while leaned");
            Assert.Greater(midLean, leaned + 0.3f, "and worse still while the lean is moving");

            float yaw0 = player.Yaw;
            int shots0 = weapon.ShotsFired;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.5f);
            s.fire = false;
            player.input = s;
            yield return new WaitForSeconds(0.6f);
            int n = weapon.ShotsFired - shots0;
            Assert.Greater(n, 3);
            Assert.Greater(player.LeanRollDegrees, 3f, "the view should be rolled to the right");
            float uprightYaw = 0f, leanedYaw = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 kick = weapon.GetRecoilKick(i, 0f, Stance.Stand);
                uprightYaw += kick.x;
                leanedYaw += Weapon.ApplyLeanToKick(kick, player.LeanRollDegrees).x;
            }
            Assert.AreEqual(leanedYaw, player.Yaw - yaw0, 0.08f, "the spray should follow the tilted gun");
            Assert.Greater(player.Yaw - yaw0, uprightYaw + 0.3f, "a right lean should drag the spray to the right");
        }

        /// <summary>Jump while pushing forward; report whether a climb started within half a second.</summary>
        IEnumerator JumpForward(System.Action<bool> climbed)
        {
            PlayerInputState s = player.input;
            s.move = new Vector2(0f, 1f);
            player.input = s;
            yield return Tap(k => { k.jumpPressed = true; return k; });
            bool vaulted = false;
            float t0 = Time.time;
            while (Time.time - t0 < 0.5f && !vaulted)
            {
                vaulted = player.IsVaulting;
                yield return null;
            }
            yield return new WaitForSeconds(0.8f);
            s.move = Vector2.zero;
            player.input = s;
            climbed(vaulted);
        }

        [UnityTest]
        public IEnumerator Space_AlwaysJumps_AndClimbingNeedsForwardIntoCover()
        {
            bool climbed = false;

            // Standing right next to cover, Space is still just a jump.
            player.Teleport(new Vector3(8f, 0.05f, -18.9f), 0f);
            yield return new WaitForSeconds(0.3f);
            float y0 = player.transform.position.y;
            yield return Tap(k => { k.jumpPressed = true; return k; });
            yield return new WaitForSeconds(0.2f);
            Assert.IsFalse(player.IsVaulting, "Space beside cover should not start a climb");
            Assert.Greater(player.transform.position.y, y0 + 0.4f, "Space beside cover should still jump");
            yield return new WaitForSeconds(1.0f);
            Assert.Less(player.transform.position.z, -18.4f);

            // Jumping into it while holding forward climbs over.
            yield return JumpForward(v => climbed = v);
            Assert.IsTrue(climbed, "jumping into low cover while holding forward should climb it");
            Assert.IsFalse(player.IsVaulting);
            Assert.Greater(player.transform.position.z, -17.7f, "the player should be on the far side");
            Assert.Less(player.transform.position.y, 0.3f);
            Assert.AreEqual(Stance.Stand, player.Stance);

            // A boarded-up window cannot be climbed through...
            Barricade window = GameObject.Find("Barricade_Window").GetComponent<Barricade>();
            player.Teleport(new Vector3(-8f, 0.05f, -8.9f), 0f);
            yield return new WaitForSeconds(0.3f);
            yield return JumpForward(v => climbed = v);
            Assert.IsFalse(climbed, "a barricaded window should block the climb");
            Assert.Less(player.transform.position.z, -8.4f);

            // ...but an open one can.
            // With its lower boards knocked out it can be climbed through, which destroys the rest.
            for (int i = 0; i < 4; i++) window.BreakPlank(i, Vector3.forward);
            Assert.IsTrue(window.IsBuilt);
            yield return null;
            player.Teleport(new Vector3(-8f, 0.05f, -8.9f), 0f);
            yield return new WaitForSeconds(0.3f);
            yield return JumpForward(v => climbed = v);
            Assert.IsTrue(climbed, "a window with its lower boards gone should be climbable");
            Assert.Greater(player.transform.position.z, -7.7f, "the player should be through the window");
            Assert.IsFalse(window.IsBuilt, "climbing through should destroy what was left of the barricade");
        }

        [Test]
        public void Sound_EveryEffectIsSynthesizedCleanly()
        {
            for (int id = 0; id < (int)SoundId.Count; id++)
            {
                for (int v = 0; v < Synth.Variants((SoundId)id); v++)
                {
                    float[] wave = Synth.Samples((SoundId)id, v);
                    Assert.Greater(wave.Length, 1000, (SoundId)id + " is too short to be a sound");
                    float peak = 0f, energy = 0f;
                    foreach (float x in wave)
                    {
                        Assert.IsFalse(float.IsNaN(x), (SoundId)id + " contains NaN");
                        peak = Mathf.Max(peak, Mathf.Abs(x));
                        energy += x * x;
                    }
                    Assert.LessOrEqual(peak, 1f, (SoundId)id + " would clip");
                    Assert.Greater(peak, 0.2f, (SoundId)id + " is nearly silent");
                    Assert.Greater(Mathf.Sqrt(energy / wave.Length), 0.005f, (SoundId)id + " has no body");
                    Assert.Less(Mathf.Abs(wave[wave.Length - 1]), 0.01f, (SoundId)id + " would click at the end");
                }
            }
        }

        [UnityTest]
        public IEnumerator Sound_FollowsWhatThePlayerDoes()
        {
            yield return new WaitForSeconds(0.3f);
            Sfx.ResetCounts();

            // Every shot is heard, and every casing lands.
            int shots0 = weapon.ShotsFired;
            PlayerInputState s = player.input;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.4f);
            s.fire = false;
            player.input = s;
            yield return null;
            int shots = weapon.ShotsFired - shots0;
            Assert.Greater(shots, 2);
            Assert.AreEqual(shots, Sfx.PlayCount(SoundId.GunShot));
            Assert.AreEqual(shots, Sfx.PlayCount(SoundId.ShellCasing));

            // Sprinting puts feet down more often than walking.
            Sfx.ResetCounts();
            s.move = new Vector2(0f, 1f);
            player.input = s;
            yield return new WaitForSeconds(1.5f);
            int walking = Sfx.PlayCount(SoundId.Footstep);
            Assert.Greater(walking, 1, "walking should make footsteps");
            player.Teleport(new Vector3(0f, 0.05f, -26f), 0f);
            Sfx.ResetCounts();
            s.sprint = true;
            player.input = s;
            yield return new WaitForSeconds(1.5f);
            Assert.Greater(Sfx.PlayCount(SoundId.Footstep), walking, "sprinting should step faster than walking");
            s.sprint = false;
            s.move = Vector2.zero;
            player.input = s;

            // A reload is heard as its parts: magazine out, magazine in.
            Sfx.ResetCounts();
            yield return Tap(k => { k.reloadPressed = true; return k; });
            float t0 = Time.time;
            while (weapon.IsReloading && Time.time - t0 < 3f) yield return null;
            Assert.AreEqual(1, Sfx.PlayCount(SoundId.MagOut));
            Assert.AreEqual(1, Sfx.PlayCount(SoundId.MagIn));
            Assert.AreEqual(0, Sfx.PlayCount(SoundId.BoltBack), "the bolt is only worked after running dry");
        }

        [UnityTest]
        public IEnumerator Sound_BendsThroughOpenings_AndIsMuffledByWalls()
        {
            yield return null;

            // Clear line of sight: nothing in the way.
            SoundPath clear = Sfx.Evaluate(new Vector3(0f, 1.5f, -20f), new Vector3(0f, 1.5f, -26f));
            Assert.AreEqual(SoundPathKind.Direct, clear.kind);
            Assert.AreEqual(0f, clear.lossDb, 0.01f);

            // Behind a solid wall next to an open doorway: the sound comes round through the door.
            Vector3 ear = new Vector3(3f, 1.5f, -10f), source = new Vector3(3f, 1.5f, -6f);
            SoundPath viaDoor = Sfx.Evaluate(source, ear);
            Assert.AreEqual(SoundPathKind.Portal, viaDoor.kind, "sound should find its way round through an opening");
            Assert.Less(viaDoor.apparentPosition.x, ear.x - 0.5f, "and be heard from the doorway's side, not through the wall");
            Assert.Greater(viaDoor.lossDb, 3f);
            Assert.Less(viaDoor.cutoffHz, 12000f, "a sound that has bent round a corner loses its top end");

            // Board the door up: whichever way it now comes, it is quieter.
            Barricade door = GameObject.Find("Barricade_Door").GetComponent<Barricade>();
            door.BuildAll();
            yield return null;
            Physics.SyncTransforms();
            SoundPath boarded = Sfx.Evaluate(source, ear);
            Assert.Greater(boarded.lossDb, viaDoor.lossDb + 1.5f, "a barricade in the doorway should muffle what comes through");

            // Through a soft wall: muffled. Through a hole in it: clean.
            SoftWall wall = GameObject.Find("SoftWall").GetComponent<SoftWall>();
            Vector3 softEar = new Vector3(-3.3f, 1.5f, -10f), softSource = new Vector3(-3.3f, 1.5f, -6f);
            SoundPath throughSheet = Sfx.Evaluate(softSource, softEar);
            Assert.AreEqual(SoundPathKind.Absorbed, throughSheet.kind);
            Assert.LessOrEqual(throughSheet.cutoffHz, 2500f);
            Assert.Greater(throughSheet.lossDb, 6f);
            wall.Punch(new Vector3(-3.3f, 1.5f, -8f), Vector3.forward);
            SoundPath throughHole = Sfx.Evaluate(softSource, softEar);
            Assert.AreEqual(SoundPathKind.Direct, throughHole.kind, "a hole in a soft wall should let sound straight through");
        }

        [UnityTest]
        public IEnumerator Sprint_StandsYouUp_AndMovesFasterThanWalking()
        {
            yield return TapCrouch();
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(Stance.Crouch, player.Stance);

            float z0 = player.transform.position.z;
            PlayerInputState s = player.input;
            s.move = new Vector2(0f, 1f);
            s.sprint = true;
            player.input = s;
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(Stance.Stand, player.Stance, "sprinting should stand the player up");
            Assert.IsTrue(player.IsSprinting);
            Assert.Greater(player.PlanarSpeed, player.walkSpeed + 1f);
            Assert.Greater(player.transform.position.z, z0 + 2f);
        }
    }
}
