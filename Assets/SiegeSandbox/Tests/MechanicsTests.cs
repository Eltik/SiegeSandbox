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
        public IEnumerator Bullets_PunchThroughSoftWalls_ButNotHardWalls()
        {
            SoftWall wall = GameObject.Find("SoftWall").GetComponent<SoftWall>();
            Target behindSoft = GameObject.Find("Target_SoftWall").GetComponent<Target>();
            int panels = wall.PanelsLeft;
            Assert.Greater(panels, 100);

            Shoot(new Vector3(-3.3f, 0.9f, -10f));
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(panels - 1, wall.PanelsLeft, "the bullet should knock out exactly one panel");
            Assert.AreEqual(100f - 34f * 0.8f, behindSoft.Health, 0.1f, "and still hit behind it at reduced damage");

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
        public IEnumerator Barricade_BreaksToBulletsAndMelee()
        {
            Barricade window = GameObject.Find("Barricade_Window").GetComponent<Barricade>();
            Assert.AreEqual(5, window.PlanksLeft);

            // Two bullets into the same plank break it.
            Shoot(new Vector3(-8f, 1.45f, -10f));
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(5, window.PlanksLeft, "one bullet should only damage a plank");
            Shoot(new Vector3(-8f, 1.45f, -10f));
            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual(4, window.PlanksLeft);

            // Melee finishes the job within three strikes.
            player.Teleport(new Vector3(-8f, 0.05f, -9f), 0f);
            yield return new WaitForSeconds(0.2f);
            for (int i = 0; i < 3 && window.IsBuilt; i++)
            {
                yield return Tap(k => { k.meleePressed = true; return k; });
                yield return new WaitForSeconds(0.6f);
            }
            Assert.IsFalse(window.IsBuilt, "three melee strikes should clear a barricade");
        }

        [UnityTest]
        public IEnumerator Interact_BuildsAndRemovesABarricade()
        {
            Barricade door = GameObject.Find("Barricade_Door").GetComponent<Barricade>();
            Interactor interactor = player.GetComponent<Interactor>();
            Assert.IsFalse(door.IsBuilt);

            player.Teleport(new Vector3(0f, 0.05f, -9.3f), 0f);
            yield return new WaitForSeconds(0.2f);
            StringAssert.Contains("barricade", interactor.Prompt);

            yield return Tap(k => { k.interactPressed = true; return k; });
            Assert.IsTrue(interactor.IsBusy);
            int shots = weapon.ShotsFired;
            PlayerInputState s = player.input;
            s.fire = true;
            player.input = s;
            yield return new WaitForSeconds(0.3f);
            s.fire = false;
            player.input = s;
            Assert.AreEqual(shots, weapon.ShotsFired, "you cannot shoot while building");

            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(door.IsBuilt, "the doorway should now be boarded up");
            Assert.AreEqual(12, door.PlanksLeft);
            Assert.IsFalse(interactor.IsBusy);

            yield return Tap(k => { k.interactPressed = true; return k; });
            yield return new WaitForSeconds(1.3f);
            Assert.IsFalse(door.IsBuilt, "interacting again should take it down");
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
            window.Remove();
            yield return null;
            yield return null;
            player.Teleport(new Vector3(-8f, 0.05f, -8.9f), 0f);
            yield return new WaitForSeconds(0.3f);
            yield return JumpForward(v => climbed = v);
            Assert.IsTrue(climbed, "an open window should be climbable");
            Assert.Greater(player.transform.position.z, -7.7f, "the player should be through the window");
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
