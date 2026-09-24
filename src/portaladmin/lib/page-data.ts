import { redirect } from "next/navigation";
import { currentPerson } from "./api";

export async function readPageData<T>(read: () => Promise<T>) {
  const [person, result] = await Promise.all([
    currentPerson(),
    read().then((data) => ({ data }), (error: unknown) => ({ error })),
  ]);

  if (!person) redirect("/sign-in");
  if ("error" in result) throw result.error;

  return { person, data: result.data };
}
