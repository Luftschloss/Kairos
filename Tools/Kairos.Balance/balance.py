#!/usr/bin/env python3
"""KAIROS offline balance lab. Standard library only; never runs AI or writes game data."""
import argparse
import copy
import csv
import hashlib
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OPS = set('DAMAGE DAMAGE_ALL HEAL DRAW SCRY PICK_FROM_TOP SUMMON_FROM_PAST DESTROY DESTROY_ALL BUFF BUFF_ALL SHIELD STASIS STASIS_ALL REFRESH SACRIFICE GAIN_SAND PAY_LIFE PUSH_MARKER PUSH_OBJECT PUSH_ALL_OBJECTS NEXT_CARD_COST PROPHECY COUNTER_DECLARED REDIRECT_ATTACK COPY_LAST_PLAYED GROW_ALL IF'.split())
CONDITIONS = set('KAIROS CHAIN_BEAT INTERJECTING TRIGGERER_IS_OPPONENT TRIGGERER_IS_SELF ANY_UNIT_DIED_THIS_BAR YOUR_LIFE_LT_OPPONENT TARGET_DIED'.split())
TRIGGERS = set('ON_PLAY ON_REVEAL ON_DEATH ON_ANY_UNIT_DEATH ON_FRIENDLY_UNIT_DEATH ON_OWN_OMEN_REVEALED ON_YOUR_CHIME ON_CHAIN_LENGTH ON_FRIENDLY_SHIELD_BROKEN ON_FRIENDLY_UNIT_ENTER ON_YOUR_WAIT'.split())
AURAS = set('COST_MOD OMEN_CAP_MOD STAT_AURA ENTER_ATK_PER_PAST_UNITS REVEAL_TOP_TO_OWNER INTERJECT_TAX_FREE'.split())
SCHOOLS = ('NEUTRAL', 'DAWN', 'NOON', 'DUSK', 'NIGHT', 'ETERNAL')
CARD_FIELDS = set('key name school type rarity time atk hp cadence keywords abilities static targets text flavor interject interjectOnly interjectCondition channel echo choice'.split())
KEEPER_FIELDS = set('key name school passive ability text'.split())
DECK_FIELDS = set('key name keeper cards'.split())
EFFECT_FIELDS = set('op target player selector duration predicted destination to from amount count pick atk hp delta minBaseTime max times costDelta fromSacrificed filter cond then else'.split())
FILTER_FIELDS = set('minAtk maxBaseTime enteredThisBar notAttacked notStasis exhausted hasChannel keyword type'.split())


def validate_filter(f):
    require(not set(f) - FILTER_FIELDS, 'unknown filter fields: ' + str(set(f) - FILTER_FIELDS))


def validate_targets(targets):
    for t in targets:
        require(not set(t) - {'id', 'selector', 'optional', 'filter'}, 'unknown target fields')
        require(t['selector'] in ('TIMELINE_OBJECT', 'ANY_CHARACTER', 'ANY_UNIT', 'ENEMY_UNIT', 'FRIENDLY_UNIT'), 'unknown target selector')
        validate_filter(t.get('filter', {}))


def require(test, message):
    if not test:
        raise ValueError(message)


def integer(value, minimum=0):
    return type(value) is int and value >= minimum


def load(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, ensure_ascii=False, separators=(',', ':')).encode()).hexdigest()


def hypergeom(n, k, draws, hits):
    """Exact without-replacement probability (converted to double only at division)."""
    require(all(integer(x) for x in (n, k, draws)) and k <= n and draws <= n, 'invalid hypergeometric population')
    if hits < 0 or hits > min(k, draws) or draws - hits > n - k:
        return 0.0
    return math.comb(k, hits) * math.comb(n - k, draws - hits) / math.comb(n, draws)


def at_least(n, k, draws, minimum=1):
    return sum(hypergeom(n, k, draws, hits) for hits in range(minimum, min(k, draws) + 1))


def mulligan(n, k, hand, minimum=1):
    # Keep all successes, return all misses BEFORE shuffling and redrawing (R-3-2).
    return sum(hypergeom(n, k, hand, kept) * at_least(n - kept, k - kept, hand - kept, max(0, minimum - kept)) for kept in range(min(k, hand) + 1))


def payoff(base, bonus, probability):
    require(0 <= probability <= 1, 'probability out of range')
    return base + probability * bonus, probability * (1 - probability) * bonus ** 2


def new_chimes(marker, cost, rung=0, bar=4, end=64):
    return [line for line in range((rung + 1) * bar, end, bar) if marker < line <= min(end, marker + cost)]


def combat(atk, hp, enemy_atk, enemy_hp, shield=False, enemy_shield=False):
    """One declared melee resolves; counterattack snapshotted, shields block positive damage only."""
    damage, counter = max(0, atk), max(0, enemy_atk)
    return {'own_hp': hp - (0 if shield else counter),
            'enemy_hp': enemy_hp - (0 if enemy_shield else damage),
            'own_shield': shield and counter == 0,
            'enemy_shield': enemy_shield and damage == 0}


def growth(atk, hp, chimes, survival):
    # Survival is an assumption, NOT inferred from the card pool. Conditional survivor stats differ from population EV.
    return {'conditional_atk': atk + chimes, 'conditional_hp': hp + chimes,
            'survival': survival ** chimes,
            'expected_live_atk': (atk + chimes) * survival ** chimes,
            'expected_live_hp': (hp + chimes) * survival ** chimes}


def validate_model(m):
    require(m.get('schema') == 1, 'unknown model schema')
    r = m['rules']
    for name, value in r.items():
        require(integer(value), 'invalid rule: ' + name)
    require(r['deck_size'] > 0 and r['bar_length'] > 0 and r['timeline_end'] > 0, 'nonpositive rule')
    require(r['horizon_max'] >= r['horizon_base'] >= r['min_action_cost'] >= 1, 'invalid horizon/floor')
    require(r['bar_length'] <= r['kairos_line_max'] < r['timeline_end'], 'invalid kairos interval')
    require(r['hand_start'] + r['second_bonus'] <= r['deck_size'], 'invalid opening')
    require(all(integer(n) and n <= r['deck_size'] for n in m['draw_samples']), 'invalid draws')
    require(all(integer(n) for n in m['growth_chimes']), 'invalid growth samples')
    require(all(0 <= p <= 1 for p in m['condition_probabilities'] + m['survival_per_chime']), 'invalid probability sample')
    require(integer(m['keeper_window_end']) and m['keeper_window_end'] < r['timeline_end'], 'invalid keeper window')
    s = m['probability_study']
    require(integer(s['base']) and integer(s['bonus']), 'invalid information sample sizes')
    require(0 <= s['probability'] <= 1 and 0 <= s['successes_in_pool'] <= s['pool_size'], 'invalid probability study')
    require(s['base'] + s['bonus'] <= s['pool_size'], 'sample exceeds remaining deck')


def condition_name(cond):
    return next(iter(cond)) if isinstance(cond, dict) else cond


def walk_effects(effects, prefix=''):
    for i, e in enumerate(effects):
        path = prefix + '/' + str(i)
        require(e.get('op') in OPS, 'unknown op at ' + path + ': ' + str(e.get('op')))
        require(not set(e) - EFFECT_FIELDS, 'unreviewed effect fields at ' + path + ': ' + str(set(e) - EFFECT_FIELDS))
        validate_filter(e.get('filter', {}))
        if e['op'] == 'IF':
            require(condition_name(e.get('cond')) in CONDITIONS, 'unknown condition at ' + path)
        yield path, e
        yield from walk_effects(e.get('then', []), path + '/then')
        yield from walk_effects(e.get('else', []), path + '/else')


def validate(data, model):
    validate_model(model)
    rules = model['rules']
    cards, keepers = {}, {}
    for kind, source, target, allowed in [('card', data['cards'], cards, CARD_FIELDS), ('keeper', data['keepers'], keepers, KEEPER_FIELDS)]:
        for entry in source:
            key = entry['key']
            require(key not in target, 'duplicate ' + key)
            require(not set(entry) - allowed, 'unreviewed ' + kind + ' fields: ' + str(set(entry) - allowed))
            require(entry['school'] in SCHOOLS, 'unknown school ' + key)
            target[key] = entry
            validate_targets(entry.get('targets', []))
            if kind == 'card':
                require(entry['type'] in ('UNIT', 'SPELL', 'SIGIL', 'OMEN'), 'unknown type ' + key)
                require(entry['rarity'] in ('COMMON', 'RARE', 'EPIC', 'LEGENDARY'), 'unknown rarity ' + key)
                require(integer(entry['time']), 'invalid cost ' + key)
                require(set(entry.get('keywords', [])) <= {'GUARD', 'SWIFT', 'GROWTH'}, 'unknown keyword ' + key)
                if entry['type'] == 'UNIT':
                    require(integer(entry['atk']) and integer(entry['hp'], 1) and integer(entry.get('cadence', 1), 1), 'invalid unit ' + key)
                for field in ('channel', 'echo'):
                    require(field not in entry or integer(entry[field], 1), 'invalid delay ' + key)
                passive = entry
            else:
                require(not set(entry['ability']) - {'name', 'cost', 'cooldownBars', 'targets', 'effects'}, 'unknown keeper ability fields')
                validate_targets(entry['ability'].get('targets', []))
                require(integer(entry['ability']['cost'], 1) and integer(entry['ability']['cooldownBars'], 1), 'invalid keeper ability ' + key)
                list(walk_effects(entry['ability']['effects'], key + '/ability/effects'))
                passive = entry.get('passive', {})
            for a in passive.get('abilities', []):
                require(not set(a) - {'trigger', 'limit', 'length', 'cond', 'effects'}, 'unknown trigger fields ' + key)
                require(a['trigger'] in TRIGGERS, 'unknown trigger ' + key)
                require('cond' not in a or condition_name(a['cond']) in CONDITIONS, 'unknown trigger condition ' + key)
                require(a.get('limit') in (None, 'ONCE_PER_YOUR_BAR'), 'unknown limit ' + key)
                list(walk_effects(a.get('effects', []), key + '/' + a['trigger']))
            for aura in passive.get('static', []):
                require(not set(aura) - {'kind', 'appliesTo', 'cond', 'limit', 'selector', 'filter', 'delta', 'atk', 'hp', 'per'}, 'unknown aura fields ' + key)
                validate_filter(aura.get('filter', {}))
                require(aura['kind'] in AURAS, 'unknown aura ' + key)
    deck_keys = set()
    for d in data['precons']:
        require(not set(d) - DECK_FIELDS and d['key'] not in deck_keys, 'invalid/duplicate deck ' + d['key'])
        deck_keys.add(d['key'])
        require(d['keeper'] in keepers and sum(d['cards'].values()) == rules['deck_size'], 'deck size/keeper: ' + d['key'])
        schools = set()
        for key, count in d['cards'].items():
            require(key in cards and integer(count, 1), 'invalid deck entry ' + key)
            c = cards[key]
            require(count <= (rules['legendary_limit'] if c['rarity'] == 'LEGENDARY' else rules['copy_limit']), 'copy limit ' + key)
            if c['school'] != 'NEUTRAL':
                schools.add(c['school'])
        require(len(schools) <= rules['max_schools'] and keepers[d['keeper']]['school'] in schools, 'deck schools: ' + d['key'])
    return cards, keepers


def apply_patch(data, patch):
    require(patch.get('schema') == 1 and patch['base_version'] == data['version'], 'patch schema/base version mismatch')
    result = copy.deepcopy(data)
    seen = set()
    for edit in patch['changes']:
        require(edit['kind'] in ('card', 'keeper', 'precon'), 'unknown patch kind')
        ident = edit['kind'], edit['key']
        require(ident not in seen, 'duplicate patch target ' + str(ident))
        seen.add(ident)
        collection = {'card': 'cards', 'keeper': 'keepers', 'precon': 'precons'}[edit['kind']]
        target = next((x for x in result[collection] if x['key'] == edit['key']), None)
        require(target is not None and 'key' not in edit['set'], 'unknown target or key mutation')
        target.update(copy.deepcopy(edit['set']))  # Top-level field replacement; arrays/objects replaced as a whole.
    result['version'] = patch['candidate_version']
    return result


def keyword(c, k):
    return k in c.get('keywords', [])


def effect_rows(data):
    rows = []
    for kind, items in [('card', data['cards']), ('keeper', data['keepers'])]:
        for c in items:
            holder = c if kind == 'card' else c.get('passive', {})
            for i, a in enumerate(holder.get('abilities', [])):
                for path, e in walk_effects(a.get('effects', []), f'abilities/{i}/effects'):
                    rows.append({'kind': kind, 'key': c['key'], 'name': c['name'], 'trigger': a['trigger'], 'limit': a.get('limit', ''), 'condition': str(a.get('cond', '')), 'path': path, 'op': e['op'], 'parameters': json.dumps(e, ensure_ascii=False, sort_keys=True), 'evaluation': 'symbolic; use scenario, not a scalar power score'})
            for i, a in enumerate(holder.get('static', [])):
                rows.append({'kind': kind, 'key': c['key'], 'name': c['name'], 'trigger': 'STATIC', 'limit': a.get('limit', ''), 'condition': a.get('cond', ''), 'path': f'static/{i}', 'op': a['kind'], 'parameters': json.dumps(a, ensure_ascii=False, sort_keys=True), 'evaluation': 'symbolic; state/target dependent'})
            if kind == 'keeper':
                for path, e in walk_effects(c['ability']['effects'], 'ability/effects'):
                    rows.append({'kind': kind, 'key': c['key'], 'name': c['name'], 'trigger': 'ACTIVE', 'limit': c['ability']['cooldownBars'], 'condition': '', 'path': path, 'op': e['op'], 'parameters': json.dumps(e, ensure_ascii=False, sort_keys=True), 'evaluation': 'symbolic; use scenario, not a scalar power score'})
            if kind == 'card' and keyword(c, 'GROWTH'):
                rows.append({'kind': kind, 'key': c['key'], 'name': c['name'], 'trigger': 'ON_YOUR_CHIME', 'limit': '', 'condition': '', 'path': 'keywords/GROWTH', 'op': 'BUFF', 'parameters': '+1/+1 PERMANENT (loader-generated)', 'evaluation': 'growth.csv conditional and survival-weighted'})
    return rows


def evaluate(data, model):
    cards, keepers = validate(data, model)
    rules = model['rules']; bar = rules['bar_length']; end = rules['timeline_end']
    result = {}
    result['cards'] = []
    result['deployment'] = []
    for c in cards.values():
        time = max(rules['min_action_cost'], c['time']); earliest = max(0, time - rules['horizon_base']) * bar
        playable = time <= rules['horizon_max'] and c['type'] != 'OMEN' and not c.get('interjectOnly')
        result['cards'].append({'key': c['key'], 'name': c['name'], 'school': c['school'], 'type': c['type'], 'rarity': c['rarity'], 'time': c['time'], 'atk': c.get('atk', ''), 'hp': c.get('hp', ''), 'cadence': c.get('cadence', ''), 'keywords': '|'.join(c.get('keywords', [])), 'ordinary_play': playable, 'earliest_unmodified_marker': earliest if playable else '', 'channel_delay': c.get('channel', ''), 'interject_tax_before_exemptions': rules['interject_tax'] if c.get('interject') else '', 'text': c.get('text', '')})
        if c['type'] != 'UNIT':
            continue
        for marker in range(bar * 3):
            if not playable or time > min(rules['horizon_max'], rules['horizon_base'] + marker // bar):
                continue
            landing = marker + time
            bells = new_chimes(marker, time, marker // bar, bar, end)
            ready = keyword(c, 'SWIFT') or bool(bells)
            result['deployment'].append({'key': c['key'], 'marker': marker, 'time': time, 'landing': landing, 'new_chimes': len(bells), 'ready_after_payment': ready, 'growth_before_next_action': len(bells) if keyword(c, 'GROWTH') else 0, 'first_attack_opportunity_marker': landing if ready else (landing // bar + 1) * bar, 'caveat': 'no response/push/death; opportunity is NOT guaranteed initiative'})
    result['growth'] = []
    for c in cards.values():
        if c['type'] == 'UNIT' and keyword(c, 'GROWTH'):
            for n in model['growth_chimes']:
                for survival in model['survival_per_chime']:
                    result['growth'].append({'key': c['key'], 'chimes': n, 'assumed_survival_per_chime': survival, 'cadence': c.get('cadence', 1), **growth(c['atk'], c['hp'], n, survival)})
    result['schools'] = []
    for school in SCHOOLS:
        pool = [c for c in cards.values() if c['school'] == school]
        units = [c for c in pool if c['type'] == 'UNIT']
        result['schools'].append({'school': school, 'kinds': len(pool), 'unit_kinds': len(units), 'early_ordinary_units': sum(c['time'] <= rules['horizon_base'] and not c.get('interjectOnly') for c in units), 'guard_kinds': sum(keyword(c, 'GUARD') for c in units), 'growth_kinds': sum(keyword(c, 'GROWTH') for c in units), 'mean_unit_cost': sum(c['time'] for c in units) / len(units) if units else 0, 'note': 'pool coverage only, not a school win-rate'})
    result['decks'], result['draws'], result['duels'], result['removal'] = [], [], [], []
    for d in data['precons']:
        counts = d['cards']; size = sum(counts.values())
        categories = {
            'unit': lambda c: c['type'] == 'UNIT',
            'ordinary_unit': lambda c: c['type'] == 'UNIT' and not c.get('interjectOnly'),
            'early_unit': lambda c: c['type'] == 'UNIT' and not c.get('interjectOnly') and c['time'] <= rules['horizon_base'],
            'guard': lambda c: keyword(c, 'GUARD'),
            'growth': lambda c: keyword(c, 'GROWTH'),
            'response_only': lambda c: bool(c.get('interjectOnly')),
        }
        stats = {name: sum(n for key, n in counts.items() if pred(cards[key])) for name, pred in categories.items()}
        result['decks'].append({'key': d['key'], 'name': d['name'], 'keeper': d['keeper'], 'size': size, **stats, 'mean_time': sum(cards[k]['time'] * n for k, n in counts.items()) / size})
        for cat, k in stats.items():
            for n in model['draw_samples']:
                result['draws'].append({'deck': d['key'], 'category': cat, 'copies': k, 'draws': n, 'expected_count': n * k / size, 'p_none': hypergeom(size, k, n, 0), 'p_at_least_one': at_least(size, k, n), 'p_at_least_two': at_least(size, k, n, 2), 'opening_keep_hits_redraw_misses_p_one': mulligan(size, k, n) if n in (rules['hand_start'], rules['hand_start'] + rules['second_bonus']) else '', 'opening_keep_hits_redraw_misses_p_two': mulligan(size, k, n, 2) if n in (rules['hand_start'], rules['hand_start'] + rules['second_bonus']) else '', 'note': 'raw sampled cards, not current hand; no burns/scry/extra draws'})
        keeper = keepers[d['keeper']]
        for key, count in counts.items():
            c = cards[key]
            if c['type'] != 'UNIT':
                continue
            aura_atk, aura_hp = 0, 0
            for a in keeper.get('passive', {}).get('static', []):
                if a['kind'] == 'STAT_AURA' and (not a.get('filter', {}).get('keyword') or keyword(c, a['filter']['keyword'])):
                    aura_atk += a.get('atk', 0); aura_hp += a.get('hp', 0)
            # Each copy-weight is exposed; never label this deterministic cross-table as a matchup win-rate.
            for opponent in data['precons']:
                if opponent['key'] == d['key']:
                    continue
                for attack_key, attack_count in opponent['cards'].items():
                    attacker = cards[attack_key]
                    if attacker['type'] != 'UNIT':
                        continue
                    enemy_keeper = keepers[opponent['keeper']]
                    aa = ah = 0
                    for a in enemy_keeper.get('passive', {}).get('static', []):
                        if a['kind'] == 'STAT_AURA' and (not a.get('filter', {}).get('keyword') or keyword(attacker, a['filter']['keyword'])):
                            aa += a.get('atk', 0); ah += a.get('hp', 0)
                    for n in model['growth_chimes']:
                        defense_atk = c['atk'] + aura_atk + (n if keyword(c, 'GROWTH') else 0)
                        defense_hp = c['hp'] + aura_hp + (n if keyword(c, 'GROWTH') else 0)
                        attack_atk = attacker['atk'] + aa + (n if keyword(attacker, 'GROWTH') else 0)
                        attack_hp = attacker['hp'] + ah + (n if keyword(attacker, 'GROWTH') else 0)
                        for shield in (False, True):
                            trade = combat(attack_atk, attack_hp, defense_atk, defense_hp, enemy_shield=shield)
                            result['duels'].append({'attacking_deck': opponent['key'], 'defending_deck': d['key'], 'attacker': attack_key, 'defender': key, 'copy_pair_weight': count * attack_count, 'chimes_each_survivor': n, 'defender_shield': shield, 'attacker_atk': attack_atk, 'attacker_hp': attack_hp, 'defender_atk': defense_atk, 'defender_hp': defense_hp, 'attacker_cadence': attacker.get('cadence', 1), 'defender_guard': keyword(c, 'GUARD'), 'kills': trade['enemy_hp'] <= 0, 'survives': trade['own_hp'] > 0, 'kill_if_atk_plus_one': not shield and attack_atk + 1 >= defense_hp, 'survive_if_hp_plus_one': attack_hp + 1 > defense_atk, 'unopposed_hits_required': math.ceil(defense_hp / attack_atk) + int(shield) if attack_atk > 0 else 'unbounded', 'own_hp_after': trade['own_hp'], 'defender_hp_after': trade['enemy_hp']})
            for n in model['growth_chimes']:
                hp = c['hp'] + aura_hp + (n if keyword(c, 'GROWTH') else 0)
                for damage in (1, 2, 3, 4, 5, 6):
                    result['removal'].append({'deck': d['key'], 'unit': key, 'copies': count, 'survived_chimes': n, 'effective_hp': hp, 'damage_packet': damage, 'dies_unshielded': damage >= hp, 'dies_shielded': False, 'damage_plus_one_changes_kill': damage < hp <= damage + 1})
    result['keepers'] = []
    for k in keepers.values():
        a = k['ability']; ready = max(0, a['cost'] - rules['horizon_base'])
        last = model['keeper_window_end'] // bar
        # Upper bound: an eligible target at every cooldown, enough initiative, inclusive window endpoints.
        opportunities = max(0, (last - ready) // a['cooldownBars'] + 1) if a['cost'] <= rules['horizon_max'] else 0
        for probability in model['condition_probabilities']:
            result['keepers'].append({'key': k['key'], 'name': k['name'], 'school': k['school'], 'ability': a['name'], 'time_per_use': a['cost'], 'cooldown_chimes': a['cooldownBars'], 'inclusive_window_end': model['keeper_window_end'], 'cooldown_opportunity_upper_bound': opportunities, 'assumed_usable_fraction': probability, 'expected_uses_under_assumption': opportunities * probability, 'expected_time_under_assumption': opportunities * probability * a['cost'], 'targets': json.dumps(a.get('targets', []), ensure_ascii=False), 'effects': json.dumps(a['effects'], ensure_ascii=False), 'passive': json.dumps(k.get('passive', {}), ensure_ascii=False), 'note': 'availability sensitivity only; not optimal use, power, or an independent binomial model'})
    result['keeper_scenarios'] = keeper_scenarios(data, model)
    result['effects'] = effect_rows(data)
    s = model['probability_study']; mean, variance = payoff(s['base'], s['bonus'], s['probability'])
    result['probability'] = []
    for label, low, high, p in [('fixed_base', s['base'], s['base'], 0), ('bounded_bonus', s['base'], s['base'] + s['bonus'], s['probability']), ('fixed_high', s['base'] + s['bonus'], s['base'] + s['bonus'], 0)]:
        result['probability'].append({'variant': label, 'mean_information_count': (1-p)*low+p*high, 'variance': p*(1-p)*(high-low)**2, 'p_find_desired': (1-p)*at_least(s['pool_size'], s['successes_in_pool'], low)+p*at_least(s['pool_size'], s['successes_in_pool'], high), 'pool_size': s['pool_size'], 'desired_copies': s['successes_in_pool'], 'note': 'not drawing cards; chance of seeing >=1 desired card in inspected top cards'})
    result['time'] = []
    for marker in range(16):
        horizon = min(rules['horizon_max'], rules['horizon_base'] + marker // bar)
        for cost in range(rules['min_action_cost'], horizon + 1):
            for gap in range(0, 9):
                opponent = marker + gap; landing = marker + cost
                result['time'].append({'marker': marker, 'opponent': opponent, 'cost': cost, 'landing': landing, 'payment_only_keeps_initiative': landing <= opponent, 'kairos': landing % bar == 0 and bar <= landing <= rules['kairos_line_max'], 'new_chimes': len(new_chimes(marker, cost, marker // bar, bar, end)), 'discount_one_landing': marker + max(rules['min_action_cost'], cost - 1), 'discount_one_kairos': (marker + max(rules['min_action_cost'], cost - 1)) % bar == 0 and bar <= marker + max(rules['min_action_cost'], cost - 1) <= rules['kairos_line_max'], 'note': 'no effects/reflux/interject; no independent probability assigned to chain/kairos'})
    return result


def keeper_scenarios(data, model):
    """Explicit input/output scenarios in native units. No attack/health/card conversion weights."""
    rows = []
    for k in data['keepers']:
        a = k['ability']; effects = [e for _, e in walk_effects(a['effects'])]
        for _, p in enumerate(model['condition_probabilities']):
            for e in effects:
                op = e['op']; unit = None; value = 0; scenario = ''
                if op == 'REFRESH':
                    unit, value, scenario = 'additional_attack_opportunities', p, 'one legal exhausted ally; p=usable before next chime/death/initiative loss; attack still pays cadence'
                elif op == 'SHIELD':
                    unit, value, scenario = 'prevented_hp_damage', 3*p, 'unshielded ally later receives one 3-damage packet with probability p; excludes destroy/debuff'
                elif op == 'PUSH_OBJECT':
                    unit, value, scenario = 'useful_object_delay_ticks', e['amount']*p, 'one enemy object; p=delay changes a relevant resolution window; NOT opponent action ticks'
                elif op == 'DRAW':
                    unit, value, scenario = 'cards_retained', e['count']*p, 'nonempty deck; p=free hand slot; excludes additional draw from payment chime'
                elif op == 'PAY_LIFE':
                    unit, value, scenario = 'life_paid', e['amount'], 'certain payment once used; never subtract from expected value of unknown future draw'
                elif op == 'NEXT_CARD_COST':
                    unit, value, scenario = 'net_ticks_saved', p*min(-e['delta'], 3-model['rules']['min_action_cost'])-a['cost'], 'next base-cost3 card; no aura; p=actually used before expiry, including payment crossing chime'
                elif op == 'SUMMON_FROM_PAST':
                    unit, value, scenario = 'recalled_bodies', p, 'one eligible past unit and free board slot with probability p; exhausted, no deployment, no raw stat credit'
                elif op == 'PICK_FROM_TOP':
                    unit, value, scenario = 'chance_find_desired', at_least(25, 8, min(e['count'], 25)), '25 remaining,8 desired; ignores p; exact find probability, not probability of winning'
                elif op == 'COPY_LAST_PLAYED':
                    unit, value, scenario = 'usable_copied_cards', p, 'opponent has public last card and free hand slot; p=copy useful after costDelta; no hidden-state inference'
                elif op in ('STASIS', 'STASIS_ALL'):
                    unit, value, scenario = 'denied_ready_windows', 2*p, 'one target initially ready; without extra refresh it misses current and first-new-chime windows; duration not global turns'
                elif op == 'PUSH_MARKER':
                    unit, value, scenario = 'raw_marker_ticks', e['amount'], 'branch-specific raw movement before caps; may BENEFIT enemy by chime/growth, not valued as pure harm'
                if unit:
                    rows.append({'keeper': k['key'], 'source': 'ACTIVE/' + op, 'assumed_p': p, 'unit': unit, 'output': value, 'time_paid_per_use': a['cost'], 'scenario': scenario})
        holder = k.get('passive', {})
        for a in holder.get('abilities', []):
            for _, e in walk_effects(a.get('effects', [])):
                if e['op'] == 'IF':
                    continue
                for n in (1, 3, 5):
                    if e['op'] == 'GROW_ALL':
                        triggers = 1 if a.get('limit') == 'ONCE_PER_YOUR_BAR' else max(1, model['rules']['bar_length']//2)
                        unit, output, note = 'total_atk_and_separately_total_hp_added', n*e['times']*triggers, f'{n} living growth units; {triggers} eligible waits per own bar, reference mutual-wait cadence2; no death; ordinary chime growth excluded'
                    elif e['op'] == 'BUFF':
                        unit, output, note = 'atk_and_hp_per_eligible_entry', f"{e.get('atk', 0)}/{e.get('hp', 0)}", 'conditional eligible entry only; do not assign unconditional 25% kairos chance'
                    else:
                        amount = e.get('amount', e.get('count', 0))
                        triggers = 1 if a.get('limit') == 'ONCE_PER_YOUR_BAR' else n
                        unit, output, note = ('sand' if e['op'] == 'GAIN_SAND' else 'life_recovered' if e['op'] == 'HEAL' else 'cards'), amount*triggers, f'{n} eligible events per own bar before cap; life/hand/resource caps may reduce gain'
                    rows.append({'keeper': k['key'], 'source': a['trigger'], 'assumed_p': '', 'unit': unit, 'output': output, 'time_paid_per_use': 0, 'scenario': note})
        for aura in holder.get('static', []):
            for n in (1, 3, 5):
                kind = aura['kind']
                if kind == 'STAT_AURA':
                    unit, output, note = 'total_max_hp_added', n*aura.get('hp', 0), f'{n} eligible guard entries; increases damage breakpoints, not damage reduction'
                elif kind == 'INTERJECT_TAX_FREE':
                    unit, output, note = 'tax_ticks_saved_upper_bound', model['rules']['interject_tax'], 'at least one response in own bar; floor can erase actual savings; never multiply by card count'
                elif kind == 'ENTER_ATK_PER_PAST_UNITS':
                    unit, output, note = 'atk_on_next_unit_entry', n//max(1, aura['per'])*aura['atk'], f'{n} unit cards in past; staircase, not fractional power'
                elif kind == 'OMEN_CAP_MOD':
                    unit, output, note = 'additional_omen_slots', aura['delta'], 'value only when previous cap binds; slots do not automatically create omens'
                else:
                    unit, output, note = 'visible_top_cards', 1, 'information only, not drawing one card; no universal scalar value'
                rows.append({'keeper': k['key'], 'source': 'STATIC/' + kind, 'assumed_p': '', 'unit': unit, 'output': output, 'time_paid_per_use': 0, 'scenario': note})
    return rows


def csv_write(path, rows):
    if not rows:
        path.write_text('', encoding='utf-8-sig'); return
    with path.open('w', encoding='utf-8-sig', newline='') as stream:
        writer = csv.DictWriter(stream, list(rows[0])); writer.writeheader(); writer.writerows(rows)


def report(data, model, results, source_hash, patch=None, baseline=None):
    lines = ['# KAIROS 数学平衡报告', '', f"- 卡池：`{data['version']}`；源数据 SHA256：`{source_hash}`", f'- 模型 SHA256：`{digest(model)}`；候选 SHA256：`{digest(patch) if patch else "无"}`', '- 方法：精确超几何概率、确定性战斗断点、时间状态枚举、条件化敏感性分析。**没有 AI，也没有估算胜率。**', '- CSV 是完整明细；本页为摘要。所有存活率、可用率都是输入假设，不是实测事实。', '', '## 构筑与起手', '', '|构筑|侍从/30|常规可部署|≤初始视界的常规侍从|守卫|蓄力|先手5张无早期侍从|后手6张无早期侍从|', '|---|---:|---:|---:|---:|---:|---:|---:|']
    for row in results['decks']:
        ds = [x for x in results['draws'] if x['deck'] == row['key'] and x['category'] == 'early_unit']
        probs = [next((x['p_none'] for x in ds if x['draws'] == n), float('nan')) for n in (model['rules']['hand_start'], model['rules']['hand_start'] + model['rules']['second_bonus'])]
        lines.append(f"|{row['name']}|{row['unit']}|{row['ordinary_unit']}|{row['early_unit']}|{row['guard']}|{row['growth']}|{probs[0]:.2%}|{probs[1]:.2%}|")
    lines += ['', '## 学派覆盖（不能当作职业强度排名）', '', '|学派|种数|侍从种数|早期常规侍从种数|守卫种数|蓄力种数|', '|---|---:|---:|---:|---:|---:|']
    for r in results['schools']:
        lines.append(f"|{r['school']}|{r['kinds']}|{r['unit_kinds']}|{r['early_ordinary_units']}|{r['guard_kinds']}|{r['growth_kinds']}|")
    lines += ['', '## 侍从交换压力（非对局胜率）', '', '假设双方侍从均存活至指定鸣钟次数、满血；应用各自钟主固定入场属性，不应用条件技能、部署、守卫目标限制或费用光环。按牌组复制数加权。分母是所有跨牌组侍从配对，不是实际遇敌分布。', '', '|攻击构筑→防守构筑|已存活鸣钟|防守有盾|一击击杀占比|击杀且存活占比|', '|---|---:|---|---:|---:|']
    groups = {}
    for r in results['duels']:
        key = r['attacking_deck'], r['defending_deck'], r['chimes_each_survivor'], r['defender_shield']
        g = groups.setdefault(key, [0, 0, 0]); w = r['copy_pair_weight']; g[0] += w; g[1] += w * r['kills']; g[2] += w * (r['kills'] and r['survives'])
    for key, (weight, kills, clean) in groups.items():
        lines.append(f'|{key[0]} → {key[1]}|{key[2]}|{key[3]}|{kills/weight:.2%}|{clean/weight:.2%}|')
    lines += ['', '## 局部概率试验（尚未实现）', '', model['probability_study']['note'], '', '|方案|期望可看牌数|方差|看到目标牌概率|', '|---|---:|---:|---:|']
    for r in results['probability']:
        lines.append(f"|{r['variant']}|{r['mean_information_count']:.2f}|{r['variance']:.2f}|{r['p_find_desired']:.2%}|")
    if patch:
        lines += ['', '## 重构候选（未同步正式数据）', '']
        for edit in patch['changes']:
            lines.append(f"- `{edit['key']}`：{edit['reason']}")
    if baseline:
        lines += ['', '## 与基线差异', '', '|构筑|侍从数变化|常规可部署变化|早期常规侍从变化|', '|---|---:|---:|---:|']
        old = {d['key']: d for d in baseline['decks']}
        for d in results['decks']:
            if d['key'] in old:
                b = old[d['key']]
                lines.append(f"|{d['name']}|{d['unit']-b['unit']:+}|{d['ordinary_unit']-b['ordinary_unit']:+}|{d['early_unit']-b['early_unit']:+}|")
    lines += ['', '## 边界与复核', '', '- effects.csv 完整列出所有 DSL 分支、被动、光环和自动蓄力；未把复合技能强行折算成攻击力。技能触发由 keepers.csv 做可用率敏感性，战斗结果仍依赖状态与操作者。', '- 不包含玩家决策分布、实际行动权争夺、手牌焚毁率、连锁解场、精确整局伤害、收藏价格或 PvE 奖励平衡。', '- 比较重构候选与正式基线时使用同一模型。不能仅凭某列接近50%宣称平衡。', '- 正式采用候选前须补：引擎语义测试、双人交叉试玩与概率机制实现。研发期按用户要求不做旧存档兼容；本工具仍不删除或修改任何存档。', '']
    return '\n'.join(lines)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--data', type=Path, default=ROOT / 'docs/data/cards.json')
    parser.add_argument('--model', type=Path, default=Path(__file__).with_name('model.json'))
    parser.add_argument('--patch', type=Path, help='isolated candidate field overrides')
    parser.add_argument('--decks', type=Path, help='JSON list of extra legal deck recipes')
    parser.add_argument('--out', required=True, type=Path, help='generated report directory (must not be Assets)')
    args = parser.parse_args(argv)
    require((ROOT / 'Assets').resolve() not in args.out.resolve().parents and args.out.resolve() != (ROOT / 'Assets').resolve(), 'reports must not overwrite/import game assets')
    model, base = load(args.model), load(args.data)
    if args.decks:
        base['precons'].extend(load(args.decks))
    baseline = evaluate(base, model)
    patch = load(args.patch) if args.patch else None
    data = apply_patch(base, patch) if patch else base
    results = evaluate(data, model) if patch else baseline
    args.out.mkdir(parents=True, exist_ok=True)
    for name, rows in results.items():
        csv_write(args.out / (name + '.csv'), rows)
    tool_hash = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    meta = {'schema': 1, 'tool_sha256': tool_hash, 'source_sha256': digest(base), 'model_sha256': digest(model), 'candidate_sha256': digest(patch) if patch else None, 'version': data['version'], 'model': model, 'candidate': patch, 'results': results}
    (args.out / 'analysis.json').write_text(json.dumps(meta, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (args.out / 'report.md').write_text(report(data, model, results, digest(base), patch, baseline if patch else None), encoding='utf-8')
    if patch:
        # Explicitly a report artifact, NOT an installable/approved balance release.
        (args.out / 'candidate-data.json').write_text(json.dumps(data, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(f"PASS {len(data['cards'])} cards, {len(data['keepers'])} keepers, {len(data['precons'])} decks; {len(results['effects'])} effect/aura nodes audited")
    print(args.out / 'report.md')
    return 0


if __name__ == '__main__':
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError) as exc:
        raise SystemExit('ERROR: ' + str(exc))
