import { redirect } from "next/navigation";
import { currentPerson } from "@/lib/api";
import { Shell } from "./shell";

export default async function Home() {
  const person = await currentPerson();
  if (!person) {
    redirect("/sign-in");
  }

  return (
    <Shell personId={person.personId}>
      <h1>Console</h1>
      <p className="lede">
        Review, decisions and email land here as they are built.
      </p>
      <div className="empty">Nothing to do yet. Applications open later.</div>
    </Shell>
  );
}
