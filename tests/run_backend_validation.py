"""Run legacy and hybrid regression suites and save a machine-readable result."""
import json
import sys
import unittest
from pathlib import Path

root = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(root / 'tests' / 'legacy'), str(root / 'tests'), str(root / 'hand-tracking')]
loader = unittest.TestLoader()
suite = unittest.TestSuite([
    loader.discover(str(root / 'tests' / 'legacy'), pattern='test_*.py'),
    loader.loadTestsFromName('test_performance_session'),
    loader.loadTestsFromName('test_hybrid_mode'),
    loader.loadTestsFromName('test_coordinate_aspect'),
    loader.loadTestsFromName('test_tracking_stability'),
    loader.loadTestsFromName('test_api_input'),
])
result = unittest.TextTestRunner(verbosity=1).run(suite)
report = {'passed': result.wasSuccessful(), 'tests': result.testsRun,
          'failures': [{'test': str(test), 'detail': detail} for test, detail in result.failures],
          'errors': [{'test': str(test), 'detail': detail} for test, detail in result.errors]}
(root / 'reports' / 'HybridBackendValidation.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
sys.exit(0 if result.wasSuccessful() else 1)
