import base64
import hashlib
import json
import unittest
from pathlib import Path

from nacl.public import PrivateKey

import avanza_sync

VECTOR = json.loads(Path(__file__).with_name("interop_vector.json").read_text(encoding="utf-8"))


class InteropTest(unittest.TestCase):
    def test_python_decrypts_what_dotnet_encrypted(self):
        private_key = PrivateKey(base64.b64decode(VECTOR["testPrivateKey"]))
        secrets = avanza_sync.decrypt(private_key, base64.b64decode(VECTOR["ciphertext"]))

        self.assertEqual(secrets, VECTOR["expected"])

    def test_fingerprints_match_between_dotnet_and_python(self):
        private_key = PrivateKey(base64.b64decode(VECTOR["testPrivateKey"]))
        public_key = base64.b64decode(VECTOR["publicKey"])

        self.assertEqual(bytes(private_key.public_key), public_key)
        self.assertEqual(avanza_sync.fingerprint(bytes(private_key.public_key)), VECTOR["fingerprint"])
        self.assertEqual(hashlib.sha256(public_key).hexdigest(), VECTOR["fingerprint"])

    def test_another_key_cannot_decrypt(self):
        with self.assertRaises(Exception):
            avanza_sync.decrypt(PrivateKey.generate(), base64.b64decode(VECTOR["ciphertext"]))


if __name__ == "__main__":
    unittest.main()
