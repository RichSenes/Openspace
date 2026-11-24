import unittest

from wake import called, choose_language


class CalledTests(unittest.TestCase):
    def test_name_then_request(self):
        self.assertEqual(called("Shiori, che tempo fa domani?"), (True, "che tempo fa domani?"))

    def test_only_the_name(self):
        self.assertEqual(called(" Shiori! "), (True, ""))

    def test_greeting_before_the_name(self):
        self.assertEqual(called("Hey Shiori, what's on today?"), (True, "what's on today?"))
        self.assertEqual(called("Senti, Shiori. Ricordami il latte."), (True, "Ricordami il latte."))
        # "Ehi" as the small model hears it.
        self.assertEqual(called("e Shiori ricordami il latte."), (True, "ricordami il latte."))

    def test_how_small_models_spell_her(self):
        self.assertEqual(called("Siori apri le note"), (True, "apri le note"))
        self.assertEqual(called("Shori."), (True, ""))

    def test_a_name_heard_a_little_wrong_is_still_her(self):
        self.assertEqual(called("Sheori, accendi la musica"), (True, "accendi la musica"))
        self.assertEqual(called("Shuri che ore sono"), (True, "che ore sono"))
        self.assertEqual(called("Siri, che ore sono"), (False, ""))
        self.assertEqual(called("Signori, si parte"), (False, ""))
        self.assertEqual(called("Sicuri di no"), (False, ""))

    def test_japanese(self):
        self.assertEqual(called("しおり、今日の予定は？"), (True, "今日の予定は？"))
        self.assertEqual(called("ねえ、シオリちゃん、おはよう"), (True, "おはよう"))

    def test_not_called(self):
        self.assertEqual(called("ho visto Shiori ieri"), (False, ""))
        self.assertEqual(called("Shioriko is a different name"), (False, ""))
        self.assertEqual(called(""), (False, ""))


class ChooseLanguageTests(unittest.TestCase):
    def test_a_short_italian_sentence_heard_as_english_stays_italian(self):
        self.assertEqual(choose_language([("en", 0.55), ("it", 0.30), ("pt", 0.1)]), "it")
        self.assertEqual(choose_language([("en", 0.85), ("it", 0.25)]), "it")

    def test_a_language_clearly_likelier_wins(self):
        self.assertEqual(choose_language([("en", 0.93), ("it", 0.04)]), "en")
        self.assertEqual(choose_language([("ja", 0.9), ("it", 0.05)]), "ja")

    def test_only_her_languages(self):
        self.assertEqual(choose_language([("pt", 0.9), ("it", 0.05)]), "it")
        self.assertEqual(choose_language([("hi", 0.99)]), "it")


if __name__ == "__main__":
    unittest.main()
