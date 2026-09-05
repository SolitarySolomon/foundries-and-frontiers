# Testing

What to check after each build, and what a pass looks like. Anything that fails, say
which line number below and what happened instead.

The mod is installed to `VintagestoryData/Mods` automatically. Only one version should
be in that folder at a time; older ones get moved to `VintagestoryData/ModsOld`.

---

## Round 1, version 0.0.3: villages exist and can be seen

### 1.1 Founding

1. `/ff village create` where you stand.
   Expect: a name, an id, culture, tier 0, claim radius 24, and **a stone cairn appears
   at your feet**.
2. Look at the cairn.
   Expect: the village name, its tier and culture, and its population.
3. `/ff village create` again without moving.
   Expect: refused, because centres must be 220 blocks apart. This is correct.
4. Walk 250 blocks away and `/ff village create norse`.
   Expect: a second village, Norse name, its own cairn.

### 1.2 Seeing the claim

5. `/ff village show`.
   Expect: the claim edge outlined on the ground, following the terrain, 24 blocks out
   in each direction from the centre.
6. `/ff village show off`.
   Expect: the outline clears.
7. `/ff village show 2`.
   Expect: the other village's claim outlined even though you are not standing in it.

### 1.3 Membership

8. `/ff spawn farmer` inside a claim.
   Expect: the reply ends with "Joined <village>."
9. `/ff village info`.
   Expect: roster of 1, and the villager listed under "Here now" as inside claim.
10. Walk outside the claim, `/ff spawn builder`, then `/ff village adopt`.
    Expect: "Nobody inside the claim needed adopting", because they are outside it.
11. Walk back inside, `/ff spawn builder`, and check it auto joined.
12. `/ff village leave` looking at a villager, then `/ff village adopt`.
    Expect: they leave, then get taken back in.

### 1.4 The cairn is not the village

13. Break the cairn.
    Expect: it drops nothing special, the village still appears in `/ff village list`,
    and `/ff village info` says the cairn is gone.
14. `/ff village mark`.
    Expect: a new cairn at the centre.

### 1.5 Persistence, the real test of B1

15. `/ff village list` and note what is there.
16. Save and quit to the main menu. Load the world again.
17. `/ff village list`.
    Expect: the same villages, same ids, same names, same rosters.
18. `/ff dump` on a villager.
    Expect: their village named, not "(missing)".

### 1.6 Removal

19. `/ff clear all`.
    Expect: villagers gone, villages **still there**. This is correct: a village is a
    record, not an entity, and it has to survive its whole population dying.
20. `/ff village remove 1`.
    Expect: village gone, its cairn gone, any villagers standing there left alive and
    unaffiliated.
21. `/ff village remove all`.

---

## Round 2, version 0.0.4: the ledger

### 2.1 Reading it

22. `/ff village create`, then `/ff village ledger`.
    Expect: six pools, all zero, and a note that no full day has been recorded yet so
    every trend reads 0.
23. `/ff village table`.
    Expect: the rules that sort items into pools, with weights.

### 2.2 Moving numbers by hand

24. `/ff village give wood 40`.
    Expect: wood stock 40, today's column +40, trend still 0.
25. `/ff village take wood 10`.
    Expect: stock 30, today +30.
26. `/ff village take wood 500`.
    Expect: **refused**, and the stock unchanged. Withdrawals never go negative.
27. `/ff village set food 100`.
    Expect: food 100, and today's column for food still 0, because setting a pool is not
    income.

### 2.3 Flow is measured, not granted

28. `/ff village day`.
    Expect: the day closes, wood trend becomes +30/day, today's column resets to 0.
29. `/ff village give wood 6`, then `/ff village day`.
    Expect: trend is now +18/day, the average of 30 and 6.
30. `/ff village day 7`.
    Expect: trend falls toward 0 as empty days push the busy ones out of the seven day
    window. This is the point: a village that stops working stops showing income.

### 2.4 Real deposits

31. `/ff give firewood 8` to put firewood in a villager's hands.
32. `/ff dump` on them.
    Expect: a "worth" line saying what pool it lands in and how much it is worth.
33. `/ff village deposit` while looking at them.
    Expect: their hands empty, wood stock rises by the weighted amount, and the reply
    names the pool.
34. `/ff give pickaxe-copper 1` then `/ff village deposit`.
    Expect: refused, because a village has no pool for a tool. It should stay in their
    hands rather than vanish.
35. Try `/ff give plank-oak 16`, `/ff give stone-granite 16`, `/ff give clay-blue 16`,
    `/ff give ingot-copper 4`, `/ff give cloth-plain 4` and deposit each.
    Expect: wood, stone, clay, metal, cloth respectively. Anything that lands in the
    wrong pool is a bug in `config/resources.json` and is a one line fix.

### 2.5 The calendar drives it

36. `/ff village ledger` and note the day number in `/ff village info`.
37. `/ff skip 3`, wait about five seconds.
    Expect: the day figure advances by 3 on its own and three days appear in the
    history. The clock checks every five real seconds, so it is not instant.

### 2.6 Persistence again

38. Give the village some stock, close a day, then save and reload.
39. `/ff village ledger`.
    Expect: stock, history and trend all exactly as they were.

---

## After any round

Anything odd, the log is the first place to look:
`VintagestoryData/Logs/server-main.log`. Errors mentioning `[F&F]` are ours.
