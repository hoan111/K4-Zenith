using CounterStrikeSharp.API.Core;

namespace Zenith_ExtendedCommands
{
	public class Weapon
	{
		public ushort WeaponID { get; }
		public string ClassName { get; }
		public gear_slot_t? Slot { get; }
		public List<string> Aliases { get; }
		public string Name { get; }

		public Weapon(ushort weaponID, string className, string name, gear_slot_t? slot, List<string> aliases)
		{
			WeaponID = weaponID;
			ClassName = className;
			Name = name;
			Slot = slot;
			Aliases = aliases;
		}

		public bool IsPrimary => Slot == gear_slot_t.GEAR_SLOT_RIFLE;
		public bool IsSecondary => Slot == gear_slot_t.GEAR_SLOT_PISTOL;
		public bool IsUtility => Slot == gear_slot_t.GEAR_SLOT_UTILITY;
		public bool IsGrenade => Slot == gear_slot_t.GEAR_SLOT_GRENADES;
		public bool IsWeapon => IsPrimary || IsSecondary;

		public static List<Weapon> List { get; } = [
			new Weapon(60, "weapon_m4a1_silencer", "M4A1-S", gear_slot_t.GEAR_SLOT_RIFLE, ["m4a1s", "m4s"]),
			new Weapon(40, "weapon_ssg08", "SSG 08", gear_slot_t.GEAR_SLOT_RIFLE, ["ssg", "scout", "ssg08"]),
			new Weapon(39, "weapon_sg556", "SG 556", gear_slot_t.GEAR_SLOT_RIFLE, ["sg556", "sg"]),
			new Weapon(38, "weapon_scar20", "SCAR-20", gear_slot_t.GEAR_SLOT_RIFLE, ["scar20", "scar"]),
			new Weapon(35, "weapon_nova", "Nova", gear_slot_t.GEAR_SLOT_RIFLE, ["nova"]),
			new Weapon(34, "weapon_mp9", "MP9", gear_slot_t.GEAR_SLOT_RIFLE, ["mp9"]),
			new Weapon(33, "weapon_mp7", "MP7", gear_slot_t.GEAR_SLOT_RIFLE, ["mp7"]),
			new Weapon(29, "weapon_sawedoff", "Sawed-Off", gear_slot_t.GEAR_SLOT_RIFLE, ["sawedoff"]),
			new Weapon(28, "weapon_negev", "Negev", gear_slot_t.GEAR_SLOT_RIFLE, ["negev"]),
			new Weapon(27, "weapon_mag7", "MAG-7", gear_slot_t.GEAR_SLOT_RIFLE, ["mag7", "mag"]),
			new Weapon(26, "weapon_bizon", "PP-Bizon", gear_slot_t.GEAR_SLOT_RIFLE, ["bizon"]),
			new Weapon(25, "weapon_xm1014", "XM1014", gear_slot_t.GEAR_SLOT_RIFLE, ["xm1014", "xm"]),
			new Weapon(24, "weapon_ump45", "UMP-45", gear_slot_t.GEAR_SLOT_RIFLE, ["ump45", "ump"]),
			new Weapon(23, "weapon_mp5sd", "MP5-SD", gear_slot_t.GEAR_SLOT_RIFLE, ["mp5sd", "mp5"]),
			new Weapon(19, "weapon_p90", "P90", gear_slot_t.GEAR_SLOT_RIFLE, ["p90"]),
			new Weapon(17, "weapon_mac10", "MAC-10", gear_slot_t.GEAR_SLOT_RIFLE, ["mac10", "mac"]),
			new Weapon(16, "weapon_m4a1", "M4A1", gear_slot_t.GEAR_SLOT_RIFLE, ["m4a1", "m4"]),
			new Weapon(14, "weapon_m249", "M249", gear_slot_t.GEAR_SLOT_RIFLE, ["m249"]),
			new Weapon(13, "weapon_galilar", "Galil AR", gear_slot_t.GEAR_SLOT_RIFLE, ["galilar", "galil"]),
			new Weapon(11, "weapon_g3sg1", "G3SG1", gear_slot_t.GEAR_SLOT_RIFLE, ["g3sg1"]),
			new Weapon(10, "weapon_famas", "FAMAS", gear_slot_t.GEAR_SLOT_RIFLE, ["famas"]),
			new Weapon(9, "weapon_awp", "AWP", gear_slot_t.GEAR_SLOT_RIFLE, ["awp"]),
			new Weapon(8, "weapon_aug", "AUG", gear_slot_t.GEAR_SLOT_RIFLE, ["aug"]),
			new Weapon(7, "weapon_ak47", "AK-47", gear_slot_t.GEAR_SLOT_RIFLE, ["ak47", "ak"]),

			new Weapon(64, "weapon_revolver", "Revolver", gear_slot_t.GEAR_SLOT_PISTOL, ["revolver"]),
			new Weapon(63, "weapon_cz75a", "CZ75-Auto", gear_slot_t.GEAR_SLOT_PISTOL, ["cz75a", "cz"]),
			new Weapon(61, "weapon_usp_silencer", "USP-S", gear_slot_t.GEAR_SLOT_PISTOL, ["usp_silencer", "usp"]),
			new Weapon(36, "weapon_p250", "P250", gear_slot_t.GEAR_SLOT_PISTOL, ["p250"]),
			new Weapon(32, "weapon_hkp2000", "P2000", gear_slot_t.GEAR_SLOT_PISTOL, ["hkp2000", "hkp"]),
			new Weapon(30, "weapon_tec9", "Tec-9", gear_slot_t.GEAR_SLOT_PISTOL, ["tec9", "tec"]),
			new Weapon(4, "weapon_glock", "Glock-18", gear_slot_t.GEAR_SLOT_PISTOL, ["glock"]),
			new Weapon(3, "weapon_fiveseven", "Five-SeveN", gear_slot_t.GEAR_SLOT_PISTOL, ["fiveseven"]),
			new Weapon(2, "weapon_elite", "Dual Berettas", gear_slot_t.GEAR_SLOT_PISTOL, ["elite"]),
			new Weapon(1, "weapon_deagle", "Desert Eagle", gear_slot_t.GEAR_SLOT_PISTOL, ["deagle"]),

			new Weapon(62, "weapon_taser", "Taser", null, ["taser"]),
			new Weapon(58, "weapon_shield", "Shield", null, ["shield"]),
			new Weapon(57, "weapon_healthshot", "Healthshot", null, ["healthshot"]),

			new Weapon(31, "weapon_flashbang", "Flashbang", gear_slot_t.GEAR_SLOT_GRENADES, ["flashbang", "flash"]),
			new Weapon(20, "weapon_smokegrenade", "Smoke Grenade", gear_slot_t.GEAR_SLOT_GRENADES, ["smokegrenade", "smoke"]),
			new Weapon(18, "weapon_molotov", "Molotov", gear_slot_t.GEAR_SLOT_GRENADES, ["molotov"]),
			new Weapon(15, "weapon_hegrenade", "HE Grenade", gear_slot_t.GEAR_SLOT_GRENADES, ["hegrenade", "grenade"]),
			new Weapon(37, "weapon_incgrenade", "Incendiary Grenade", gear_slot_t.GEAR_SLOT_GRENADES, ["incgrenade"]),
			new Weapon(41, "weapon_decoy", "Decoy Grenade", gear_slot_t.GEAR_SLOT_GRENADES, ["decoy"]),
			new Weapon(42, "weapon_tagrenade", "Tactical Grenade", gear_slot_t.GEAR_SLOT_GRENADES, ["tagrenade"]),
		];
	}
}
