# SPDX-License-Identifier: Apache-2.0
# The SDK scenario against a running Askwerk (run by src/backend/Askwerk.Tests.Api/SdkTests.cs): python tests/scenario.py
import os
import sys
import uuid

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))

from askwerk import Askwerk, AskwerkError, verify_webhook  # noqa: E402

env = os.environ
base_url = env["ASKWERK_URL"]
survey = env["ASKWERK_SURVEY"]
askwerk = Askwerk(base_url, api_key=env["ASKWERK_KEY"])

assert [item["id"] for item in askwerk.surveys.list_all(status="published")] == [survey]
assert askwerk.surveys.get(survey)["title"] == "Customer satisfaction 2026"
assert any(variable["variable"] == "BEFORE" for variable in askwerk.responses.codebook(survey, language="de")["variables"])

responses = list(askwerk.responses.list_all(survey))
assert len(responses) == 1
assert any(answer["variable"] == "SAT" and answer["value"] == "4" for answer in responses[0]["answers"])

started = askwerk.exports.start(survey, {"format": "csv"})
ready = askwerk.exports.wait_until_ready(started["id"], interval=0.2, timeout=60)
assert len(askwerk.exports.download(ready["id"])) > 0

created = askwerk.webhooks.create({"url": "https://hooks.example.org/askwerk", "events": ["survey.published"]})
assert any(each["id"] == created["webhook"]["id"] for each in askwerk.webhooks.list()["items"])
askwerk.webhooks.delete(created["webhook"]["id"])

try:
    askwerk.surveys.get(str(uuid.uuid4()))
    raise AssertionError("a missing survey must raise")
except AskwerkError as error:
    assert (error.status, error.type) == (404, "https://askwerk.org/problems/survey-not-found"), (error.status, error.type)

client = Askwerk(base_url, client_id=env["ASKWERK_CLIENT_ID"], client_secret=env["ASKWERK_CLIENT_SECRET"])
assert len(client.surveys.list()["items"]) == 1

assert verify_webhook("whsec_test", env["ASKWERK_SIGNATURE"], env["ASKWERK_SIGNED_BODY"])
assert not verify_webhook("whsec_test", env["ASKWERK_SIGNATURE"], env["ASKWERK_SIGNED_BODY"] + " ")
print("Python SDK: ok")
