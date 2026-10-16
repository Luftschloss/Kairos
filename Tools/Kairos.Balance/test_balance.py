import copy
import itertools
import json
from pathlib import Path
import tempfile
import unittest

import balance as b


class MathTests(unittest.TestCase):
    def test_hypergeometric_matches_exhaustive_subsets(self):
        for n in range(1, 9):
            for k in range(n + 1):
                for draws in range(n + 1):
                    samples = list(itertools.combinations(range(n), draws))
                    for hits in range(draws + 1):
                        exact = sum(sum(x < k for x in s) == hits for s in samples) / len(samples)
                        self.assertAlmostEqual(exact, b.hypergeom(n, k, draws, hits))
                    self.assertAlmostEqual(1, sum(b.hypergeom(n, k, draws, h) for h in range(draws + 1)))

    def test_mulligan_matches_two_stage_enumeration(self):
        n, k, hand = 6, 2, 3
        for minimum in (1, 2):
            first = list(itertools.combinations(range(n), hand)); total = 0
            for sample in first:
                kept = [x for x in sample if x < k]
                remaining = [x for x in range(n) if x not in kept]
                draws = list(itertools.combinations(remaining, hand - len(kept)))
                total += sum(len(kept) + sum(x < k for x in s) >= minimum for s in draws) / len(draws) / len(first)
            self.assertAlmostEqual(total, b.mulligan(n, k, hand, minimum))

    def test_probability_edge_cases(self):
        self.assertEqual(1, b.hypergeom(0, 0, 0, 0))
        self.assertEqual(0, b.at_least(30, 0, 5))
        self.assertEqual(1, b.at_least(30, 30, 5))
        self.assertEqual(0, b.mulligan(30, 0, 5))
        self.assertEqual(1, b.mulligan(30, 30, 5))
        self.assertEqual(1, b.at_least(30, 0, 0, 0))
        with self.assertRaises(ValueError): b.hypergeom(30, 4, 31, 0)

    def test_low_risk_probability_mean_variance(self):
        self.assertEqual((1.5, .25), b.payoff(1, 1, .5))
        self.assertEqual((1, 0), b.payoff(1, 1, 0))
        self.assertEqual((2, 0), b.payoff(1, 1, 1))
        with self.assertRaises(ValueError): b.payoff(1, 1, 2)

    def test_simultaneous_trade_shield_zero_attack(self):
        trade = b.combat(3, 2, 2, 3)
        self.assertEqual((0, 0), (trade['own_hp'], trade['enemy_hp']))
        trade = b.combat(3, 2, 2, 3, enemy_shield=True)
        self.assertEqual((0, 3), (trade['own_hp'], trade['enemy_hp']))
        self.assertFalse(trade['enemy_shield'])
        self.assertTrue(b.combat(0, 2, 0, 3, True, True)['enemy_shield'])
        self.assertEqual(2, b.combat(-2, 2, -3, 3)['own_hp'])

    def test_chimes_do_not_repeat_or_ring_at_end(self):
        self.assertEqual([4, 8], b.new_chimes(3, 6))
        self.assertEqual([], b.new_chimes(7, 1, 2))
        self.assertEqual([], b.new_chimes(63, 1, 15))
        self.assertEqual([60], b.new_chimes(59, 99, 14))

    def test_growth_conditioning_is_not_free_survival(self):
        a = b.growth(1, 3, 3, .5)
        self.assertEqual(4, a['conditional_atk'])
        self.assertEqual(6, a['conditional_hp'])
        self.assertEqual(.125, a['survival'])
        self.assertEqual(.5, a['expected_live_atk'])


class DataTests(unittest.TestCase):
    def setUp(self):
        self.data = b.load(b.ROOT / 'docs/data/cards.json')
        self.model = b.load(Path(__file__).with_name('model.json'))
        self.patch = b.load(Path(__file__).with_name('candidate-v02.json'))

    def test_full_pool_and_candidate_valid_and_source_unchanged(self):
        before = b.digest(self.data)
        self.assertEqual((46, 10), tuple(map(len, b.validate(self.data, self.model))))
        patched = b.apply_patch(self.data, self.patch)
        b.validate(patched, self.model)
        self.assertEqual(before, b.digest(self.data))
        results = b.evaluate(patched, self.model)
        self.assertEqual([18, 18], [r['unit'] for r in results['decks']])
        self.assertEqual([10, 10], [r['early_unit'] for r in results['decks']])
        self.assertEqual(10, len(set(r['keeper'] for r in results['keeper_scenarios'])))

    def test_baseline_counts_and_probabilities(self):
        results = b.evaluate(self.data, self.model)
        self.assertEqual([12, 20], [r['unit'] for r in results['decks']])
        self.assertEqual([10, 20], [r['ordinary_unit'] for r in results['decks']])
        self.assertEqual([8, 10], [r['early_unit'] for r in results['decks']])
        row = next(x for x in results['draws'] if x['deck'] == 'PRECON_DAWN_NIGHT' and x['category'] == 'early_unit' and x['draws'] == 5)
        self.assertAlmostEqual(0.1847922192749779, row['p_none'])
        self.assertGreater(row['opening_keep_hits_redraw_misses_p_one'], row['p_at_least_one'])

    def test_all_effect_nodes_including_conditions_and_growth_exported(self):
        rows = b.effect_rows(self.data)
        self.assertEqual(85, len(rows))
        self.assertEqual(3, sum(r['path'] == 'keywords/GROWTH' for r in rows))
        self.assertTrue(any('/then/' in r['path'] for r in rows))
        self.assertTrue(any('/else/' in r['path'] for r in rows))
        self.assertEqual(10, len({r['key'] for r in rows if r['kind'] == 'keeper'}))

    def test_unknown_mechanics_fail_closed(self):
        for field, val in [('criticalChance', .5), ('keywords', ['DODGE'])]:
            data = copy.deepcopy(self.data); data['cards'][0][field] = val
            with self.assertRaises(ValueError): b.validate(data, self.model)
        data = copy.deepcopy(self.data)
        data['cards'][0]['abilities'][0]['effects'][0]['probability'] = .5
        with self.assertRaises(ValueError): b.validate(data, self.model)
        data = copy.deepcopy(self.data)
        data['cards'][0]['abilities'][0]['effects'][0]['op'] = 'RANDOM_DAMAGE'
        with self.assertRaises(ValueError): b.validate(data, self.model)

    def test_invalid_deck_and_patch_are_rejected(self):
        for change in ('size', 'copies', 'unknown', 'school'):
            data = copy.deepcopy(self.data)
            deck = data['precons'][0]
            if change == 'size': deck['cards']['DAWN_SCOUT'] = 1
            if change == 'copies': deck['cards']['DAWN_SCOUT'] = 3; deck['cards']['DAWN_COURIER'] = 1
            if change == 'unknown': deck['cards']['NO_SUCH_CARD'] = deck['cards'].pop('DAWN_SCOUT')
            if change == 'school': deck['keeper'] = 'K_DUSK_MORGAN'
            with self.assertRaises(ValueError): b.validate(data, self.model)
        patch = copy.deepcopy(self.patch); patch['base_version'] = 'other'
        with self.assertRaises(ValueError): b.apply_patch(self.data, patch)
        patch = copy.deepcopy(self.patch); patch['changes'].append(patch['changes'][0])
        with self.assertRaises(ValueError): b.apply_patch(self.data, patch)

    def test_model_overrides_drive_payment_and_kairos(self):
        self.model['rules']['interject_tax'] = 3
        self.model['rules']['kairos_line_max'] = 8
        self.model['rules']['min_action_cost'] = 2
        result = b.evaluate(self.data, self.model)
        strike = next(r for r in result['cards'] if r['key'] == 'DAWN_FIRST_LIGHT_STRIKE')
        self.assertEqual(3, strike['interject_tax_before_exemptions'])
        self.assertTrue(all(r['cost'] >= 2 for r in result['time']))
        self.assertTrue(all(not r['kairos'] for r in result['time'] if r['landing'] > 8))
        self.assertTrue(all(not r['discount_one_kairos'] for r in result['time'] if r['discount_one_landing'] > 8))

    def test_regeneration_is_deterministic(self):
        self.assertEqual(b.digest(b.evaluate(self.data, self.model)), b.digest(b.evaluate(self.data, self.model)))

    def test_cli_writes_complete_results_to_temp_and_never_assets(self):
        scratch = b.ROOT / 'Library/Locus/tmp'; scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=scratch) as directory:
            self.assertEqual(0, b.main(['--out', directory, '--patch', str(Path(__file__).with_name('candidate-v02.json'))]))
            outputs = list(Path(directory).glob('*.csv'))
            self.assertEqual(13, len(outputs))
            self.assertTrue((Path(directory) / 'report.md').exists())
            self.assertTrue((Path(directory) / 'candidate-data.json').exists())
        with self.assertRaises(ValueError): b.main(['--out', str(b.ROOT / 'Assets/Kairos/Data')])


if __name__ == '__main__':
    unittest.main()
