"""Regression tests for MCP extension method registration guards."""

import unittest

from extension_contract import (
    METHOD,
    build_core_method_binding,
    build_duplicate_server,
    build_unreachable_binding,
    build_valid_server,
)


class ExtensionContractTests(unittest.TestCase):
    """Keep invalid method ownership from reaching runtime dispatch."""

    def test_one_extension_can_own_vendor_method(self) -> None:
        self.assertIsNotNone(build_valid_server())

    def test_two_extensions_cannot_own_same_method(self) -> None:
        with self.assertRaisesRegex(ValueError, METHOD):
            build_duplicate_server()

    def test_extension_cannot_claim_core_method(self) -> None:
        with self.assertRaisesRegex(ValueError, "tools/list"):
            build_core_method_binding()

    def test_method_must_target_a_protocol_version(self) -> None:
        with self.assertRaisesRegex(ValueError, "protocol_versions"):
            build_unreachable_binding()


if __name__ == "__main__":
    unittest.main()
