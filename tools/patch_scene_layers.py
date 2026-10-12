import os, re
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))
p = 'Game/FmAct1Story.cs'
s = open(p, encoding='utf-8').read()

PRE = '''    // ---------------- before the boss ----------------
    public static async Task PreBoss()
    {
        if (await FmScene.Open() is not { } s) return;
        try
        {
            Music(PlazaTrack);
            await s.Still("act1_plaza_teana", "teana");
            await s.Say("tea_lots"); await s.Say("tea_mages");
            await s.Still("act1_procession");
            await s.Say("tea_awesome"); await s.Say("tea_spooky"); await s.Say("tea_heishin");
            await s.Say("tea_kid"); await s.Say("tea_attitude"); await s.Say("tea_world");
            await s.Hide();
            await s.Say("tea_lookjono");
            await s.Still("act1_jono_seto", "jono", "seto");
            await s.Say("jono_lost"); await s.Say("seto_speck"); await s.Say("jono_saywhat"); await s.Say("seto_forgive");
            await s.Bg("bg_cols"); await s.Chr("chr_jono_cols", "jono");
            await s.Say("tea_heyjono"); await s.Lit("jono", $"Teana and {Name}...");
            await s.Say("tea_whatsup"); await s.Say("jono_whaddaya"); await s.Say("tea_youlost"); await s.Say("jono_blunt");
            await s.Chr("chr_seto_cols", "seto");
            await s.Say("seto_weary"); await s.Say("seto_worthy"); await s.Say("jono_nerve");
            await s.Lit("teana", $"Hey, {Name}...\\nWhy don't you challenge him?");
            await s.Say("tea_afterall"); await s.Say("tea_bet");
            await s.Lit("jono", $"Yeah, {Name}!\\nDo it! Show him who's\\nboss around here!");
            await s.Say("seto_victim"); await s.Say("seto_hope");
            await s.Say("guard_calls"); await s.Say("seto_isee"); await s.Say("seto_postponed");
            await s.Say("seto_frequent"); await s.Say("jono_right"); await s.Say("seto_waitthere"); await s.Say("seto_name");
            await s.Chr("chr_jono_cols", "jono");
            await s.Say("jono_believe"); await s.Say("tea_charmer");
            await s.Lit("jono", $"I got a feeling {Name}\\nwill put him in his place.\\nWon't ya, buddy?");
            await s.Hide(0.8);
            // the duel ground
            Music(DuelGroundTrack);
            await s.Bg("bg_room"); await s.Chr("chr_jono_room", "jono");
            await s.Say("jono_wait"); await s.Say("jono_tough"); await s.Say("jono_listen"); await s.Say("jono_fact");
            await s.Say("jono_butmage"); await s.Say("jono_chance");
            await s.Lit("jono", $"Listen, {Name}...\\nDon't underestimate the guy.");
            await s.Say("jono_sloppy"); await s.Say("jono_there");
            // Seto arrives with his guards: the picture pans up from his feet
            await s.Pan("act1_seto_guards_tall");
            await s.Say("jono_lookat"); await s.Say("jono_groupies"); await s.Say("jono_whip");
            await s.Lit("teana", $"Do it, {Name}!");
            await s.Bg("bg_room"); await s.Chr("chr_seto_room", "seto");
            await s.Say("seto_entertain"); await s.Say("seto_enjoy"); await s.Say("seto_disappoint");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 pre-boss scene: " + ex); }
        await s.Close();
    }
'''

AFTER_SETO = '''            try
            {
                Music(DuelGroundTrack);
                await s.Bg("bg_room"); await s.Chr("chr_seto_room", "seto");
                await s.Say("seto_cannot"); await s.Say("seto_wait"); await s.Say("seto_hmm"); await s.Say("seto_enough");
                await s.Say("seto_paths"); await s.Say("seto_farewell");
                await s.Chr("chr_jonoteana_room", "jono", "teana");
                await s.Lit("jono", $"You did it! {Name}!"); await s.Say("jono_didja");
                await s.Lit("teana", $"{Name}...\\nYou're just too good!");
                await s.Lit("jono", "\\"I bid you farewell\\"...\\nHah! What a loser!");
                await s.Hide(1.2);
                // the palace that night
                Music(PalaceTrack);
                await s.Bg("bg_gates", 0.8);
                await s.Wait(1.0);
                await s.Chr("chr_simon_gates", "simon");
                await s.Say("simon_room"); await s.Say("simon_ready");
                await s.Still("act1_simon_study", "simon");
                await s.Say("simon_annoy"); await s.Say("serv_trouble"); await s.Say("serv_mage"); await s.Say("serv_power");
                await s.Say("simon_what"); await s.Say("serv_sorcery"); await s.Say("simon_where"); await s.Say("serv_hall"); await s.Say("simon_dog");
                Music(HeishinTrack);
                await s.Bg("bg_ruin");
                await s.Say("simon_whatthe");
                await s.Still("act1_heishin_ruins", "heishin");
                await s.Say("hei_goodsee"); await s.Say("simon_revolt"); await s.Say("hei_mad"); await s.Say("hei_throne");
                await s.Say("simon_throne"); await s.Say("hei_power"); await s.Say("simon_power"); await s.Say("hei_recog");
                await s.Say("simon_dark"); await s.Say("hei_treasure");
                await s.Still("act1_heishin_rod", "heishin");
                await s.Say("hei_taste");
                await s.Flash();
                await s.Bg("bg_hall");
                await s.Wait(0.6);
                await s.Chr("chr_servant_hall", "servant");
                await s.Lit("servant", $"Prince {Name}!"); await s.Say("isis_invaded"); await s.Say("isis_leave");
                await s.Hide();
                await s.Still("act1_seto_men", "seto", "servant");
                await s.Say("isis_no");
                await s.Lit("seto", $"Prince {Name}...\\nDid you hope to escape me?");
                await s.Say("seto_meet"); await s.Say("seto_away"); await s.Say("seto_speak"); await s.Say("guard_cmd");
                Music(SetoNightTrack);
                await s.Bg("bg_pillars"); await s.Chr("chr_seto_pillars", "seto");
                await s.Lit("seto", $"Why do you flee?\\n{Name}.");
                await s.Say("seto_captive"); await s.Say("seto_provoke"); await s.Say("seto_want"); await s.Say("seto_iwant");
                await s.Say("seto_location"); await s.Say("seto_spare"); await s.Say("seto_tellme");
                int tell = await Choose(s, 0, "Refuse", "Tell");
                await s.Say(tell == 0 ? "seto_fool" : "seto_talk");
                await s.Say("simon_myprince"); await s.Say("seto_simon");
                await s.Still("act1_simon_puzzle");
                await s.Say("simon_item"); await s.Say("simon_takeit");
                await s.Lit("k", $"{Name} gets Millennium Puzzle!");
                await s.Bg("bg_pillars"); await s.Chr("chr_simon_pillars", "simon");
                await s.Say("simon_keep"); await s.Say("simon_key"); await s.Say("simon_wicked");
                await s.Say("seto_wellwell"); await s.Say("seto_handkey"); await s.Say("simon_cur"); await s.Say("simon_leave");
                await s.Say("seto_outway"); await s.Say("simon_run");
                if (await Choose(s, 0, "Run", "Stay") == 1) await s.Say("simon_listen");
                Music(HeishinTrack);
                await s.Chr("chr_heishin_pillars", "heishin");
                await s.Say("hei_donotmove"); await s.Say("hei_found"); await s.Say("seto_yes"); await s.Say("hei_escape");
                await s.Say("hei_resist"); await s.Say("hei_handover");
                await s.Chr("chr_simon_pillars", "simon");
                await s.Say("simon_option"); await s.Say("simon_battle");
                // FM keeps asking until you agree to the duel
                while (await Choose(s, 0, "Duel", "Pass") == 1)
                {
                    await s.Say("simon_confront"); await s.Say("simon_unleash"); await s.Say("simon_face");
                }
                await s.Chr("chr_heishin_pillars", "heishin");
                await s.Say("hei_wasted");
            }
'''

AFTER_HEI = '''        try
        {
            Music(LossTrack);
            await s.Bg("bg_pillars"); await s.Chr("chr_heishin_pillars", "heishin");
            await s.Say("hei_foolish"); await s.Say("hei_rip");
            await s.Still("act1_heishin_simon");
            await s.Say("hei_orelse"); await s.Say("simon_shatter"); await s.Say("simon_vile"); await s.Say("hei_hands");
            await s.Say("simon_puzzle"); await s.Say("simon_dontgive");
            // Simon won't take no for an answer
            while (await Choose(s, 1, "Don't shatter the puzzle", "Shatter the puzzle") == 0)
                await s.Lit("simon", "But Prince.....\\nThe Puzzle");
            await s.Still("act1_puzzle_shatter");
            await s.Say("hei_no");
            await s.Flash();
            Music(PuzzleTrack);
            await s.Bg("bg_inside", 1.0);
            await s.Wait(1.0);
            await s.Chr("chr_simon_inside", "simon");
            await s.Say("simon_awoken"); await s.Say("simon_precise"); await s.Say("simon_sorry"); await s.Say("simon_allow");
            await s.Say("simon_destroy"); await s.Say("simon_oneday"); await s.Say("simon_sleep"); await s.Say("simon_someone");
            await s.Say("simon_guide");
            await s.Lit("simon", "I won't be able to reach you\\nwhen you wake up, but please\\ntake the remainder of my power.");
            // Simon's gift: a Bright Castle, picked up like a card reward
            s.SetVisible(false);
            await GiveBrightCastle(player);
            s.SetVisible(true);
            await s.Say("simon_rest");
            await s.Caption("World Tournament and Final Arc (Act 2 and 3) coming soon, for now Acts 2 and 3 will not include cutscenes but battles can still be played till the end! Good luck!", 2.0);
        }
'''

a = s.index('    // ---------------- before the boss ----------------')
b = s.index('    // ---------------- after beating Young Priest Seto ----------------')
s = s[:a] + PRE + '\n' + s[b:]
a = s.index('            try\n            {\n                Music(DuelGroundTrack);')
b = s.index('            catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 ending scene: " + ex); }')
s = s[:a] + AFTER_SETO + s[b:]
a = s.index('        try\n        {\n            Music(LossTrack);')
b = s.index('        catch (Exception ex) { Log.Warn("[FMDuelist] Act 1 puzzle scene: " + ex); }')
s = s[:a] + AFTER_HEI + s[b:]
open(p, 'w', encoding='utf-8').write(s)
assert 's.Show(' not in s, 'old Show calls left'
print('ok')
