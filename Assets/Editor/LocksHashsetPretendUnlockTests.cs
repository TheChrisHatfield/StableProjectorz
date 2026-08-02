using NUnit.Framework;
using spz;

/// <summary>
/// Force_KeepRenderingCameras uses keep_pretending_isLocked. Real Lock→Unlock under pretend
/// must not fire unlocked (would Destroy content/depth RTs mid Gen Art).
/// </summary>
public sealed class LocksHashsetPretendUnlockTests {

	[Test]
	public void Unlock_WhilePretending_DoesNotFireUnlocked() {
		var locks = new LocksHashset_OBJ();
		int locked = 0, unlocked = 0;
		locks.onLockStatusChanged += isLocked => {
			if (isLocked) locked++;
			else unlocked++;
		};

		locks.keep_pretending_isLocked(true);
		Assert.That(locked, Is.EqualTo(1));
		Assert.That(locks.isLocked(), Is.True);

		object owner = new object();
		locks.Lock(owner);
		Assert.That(locked, Is.EqualTo(1), "second Lock under pretend must not re-fire locked");

		locks.Unlock(owner);
		Assert.That(unlocked, Is.EqualTo(0), "Unlock under pretend must not Destroy RTs");
		Assert.That(locks.isLocked(), Is.True);

		locks.keep_pretending_isLocked(false);
		Assert.That(unlocked, Is.EqualTo(1));
		Assert.That(locks.isLocked(), Is.False);
	}

	[Test]
	public void Unlock_WithoutPretend_FiresUnlocked() {
		var locks = new LocksHashset_OBJ();
		int unlocked = 0;
		locks.onLockStatusChanged += isLocked => {
			if (!isLocked) unlocked++;
		};
		object owner = new object();
		locks.Lock(owner);
		locks.Unlock(owner);
		Assert.That(unlocked, Is.EqualTo(1));
	}
}
