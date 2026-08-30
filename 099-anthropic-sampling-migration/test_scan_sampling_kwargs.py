import unittest
from pathlib import Path

from scan_sampling_kwargs import scan_paths, scan_source


class SamplingScannerTests(unittest.TestCase):
    def test_finds_direct_keywords(self) -> None:
        findings = scan_source(
            "client.messages.create(temperature=0, top_k=20)", Path("direct.py")
        )
        self.assertEqual(
            ["temperature", "top_k"], [item.parameter for item in findings]
        )

    def test_finds_literal_dictionary_expansion(self) -> None:
        findings = scan_source(
            'client.messages.create(**{"top_p": 0.9})', Path("literal.py")
        )
        self.assertEqual(["top_p"], [item.parameter for item in findings])
        self.assertEqual("expanded dictionary", findings[0].source)

    def test_finds_named_dictionary_expansion(self) -> None:
        findings = scan_source(
            'options = {"top_k": 40}\nclient.messages.create(**options)',
            Path("named.py"),
        )
        self.assertEqual(["top_k"], [item.parameter for item in findings])

    def test_finds_beta_and_response_wrapper_calls(self) -> None:
        source = """client.beta.messages.stream(top_p=1)
client.messages.with_raw_response.create(temperature=0)
"""
        findings = scan_source(source, Path("wrappers.py"))
        self.assertEqual(
            ["top_p", "temperature"], [item.parameter for item in findings]
        )

    def test_ignores_unrelated_temperature(self) -> None:
        source = """temperature = 21
thermostat.set(temperature=temperature)
client.messages.create(max_tokens=64)
"""
        self.assertEqual([], scan_source(source, Path("unrelated.py")))

    def test_ignores_batch_method_direct_keywords(self) -> None:
        source = "client.messages.batches.create(requests=[], temperature=0)"
        self.assertEqual([], scan_source(source, Path("batch.py")))

    def test_reassignment_clears_known_dictionary(self) -> None:
        source = """options = {"top_p": 1}
options = build_options()
client.messages.create(**options)
"""
        self.assertEqual([], scan_source(source, Path("dynamic.py")))

    def test_missing_path_is_an_error(self) -> None:
        findings, errors = scan_paths([Path("definitely-not-present.py")])
        self.assertEqual([], findings)
        self.assertEqual(1, len(errors))
        self.assertIn("path does not exist", errors[0])

    def test_function_parameter_shadows_outer_dictionary(self) -> None:
        source = """options = {"top_p": 1}
def send(options):
    return client.messages.create(**options)
"""
        self.assertEqual([], scan_source(source, Path("shadowed.py")))


if __name__ == "__main__":
    unittest.main()
